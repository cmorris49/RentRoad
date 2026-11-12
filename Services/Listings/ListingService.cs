using RentRoad.Localization;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RentRoad.Data;
using RentRoad.Models;
using RentRoad.Models.Enums;

namespace RentRoad.Services.Listings;

/// <summary>
/// Applies owner listing rules and retrieves published listing data without exposing private accounts.
/// </summary>
public sealed partial class ListingService : IListingService
{
    private static string SignInMessage => SharedResource.Get("ListingValidation.SignInToManageYourListings");
    private readonly ApplicationDbContext context;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes listing management with the scoped database context and UTC clock.
    /// </summary>
    public ListingService(ApplicationDbContext context, TimeProvider timeProvider)
    {
        this.context = context;
        this.timeProvider = timeProvider;
    }

    /// <summary>Returns only whether the authenticated viewer owns this listing, using the database's Identity comparison semantics.</summary>
    public Task<bool> IsOwnedByAsync(int listingId, ClaimsPrincipal? user, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actorId = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return listingId <= 0 || string.IsNullOrWhiteSpace(actorId) ? Task.FromResult(false)
            : context.Listings.AsNoTracking().AnyAsync(listing => listing.ListingId == listingId && listing.OwnerId == actorId, cancellationToken);
    }

    /// <summary>
    /// Creates an empty draft associated with the authenticated, existing owner.
    /// </summary>
    public Task<ListingServiceResult<Listing>> CreateDraftAsync(ClaimsPrincipal? user,
        CancellationToken cancellationToken = default) => CreateDraftAsync(user, new ListingUpdate(), cancellationToken);

    /// <summary>
    /// Creates a draft with validated details in one save, using the existing draft rules and immutable owner association.
    /// </summary>
    public async Task<ListingServiceResult<Listing>> CreateDraftAsync(ClaimsPrincipal? user, ListingUpdate? details,
        CancellationToken cancellationToken = default)
    {
        var ownerId = GetAuthenticatedOwnerId(user);
        if (ownerId is null)
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.Unauthenticated,
                new ListingValidationError(nameof(Listing.OwnerId), SignInMessage));
        }

        if (!await context.Users.AnyAsync(owner => owner.Id == ownerId, cancellationToken))
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.NotFound,
                new ListingValidationError(nameof(Listing.OwnerId), SharedResource.Get("ListingValidation.YourAccountCouldNotBeFound")));
        }

        if (details is null)
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.ValidationFailed,
                new ListingValidationError(string.Empty, SharedResource.Get("ListingValidation.ListingDetailsAreRequired")));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var listing = new Listing
        {
            OwnerId = ownerId,
            Status = ListingStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        ApplyEditableDetails(details, listing);
        var windows = details.AvailabilityWindows?.ToArray();
        var errors = ValidateDraft(listing, windows);
        if (errors.Count > 0)
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.ValidationFailed, errors.ToArray());
        }
        if (windows is not null)
        {
            listing.AvailabilityWindows = windows.Select(window => new AvailabilityWindow
                { StartDate = window.StartDate, EndDate = window.EndDate }).ToList();
        }
        context.Listings.Add(listing);
        await context.SaveChangesAsync(cancellationToken);
        return ListingServiceResult<Listing>.Success(listing);
    }

    /// <summary>
    /// Reads one owned listing with a separate tracker, without loading every owned listing or validating publication.
    /// </summary>
    public async Task<ListingServiceResult<Listing>> GetOwnerListingAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        await using var readContext = ListingOperationScope.CreateContext(context);
        readContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return await ListingOperationScope.GetOwnedListingAsync(readContext, listingId, user, cancellationToken);
    }

    /// <summary>
    /// Updates an owner's editable details, protecting historical records and reservations during window replacement.
    /// </summary>
    public Task<ListingServiceResult<Listing>> UpdateAsync(int listingId, ClaimsPrincipal? user,
        ListingUpdate? details, CancellationToken cancellationToken = default)
    {
        if (details?.AvailabilityWindows is null)
        {
            return UpdateListingDetailsAsync(listingId, user, details, cancellationToken);
        }
        var snapshot = details with { AvailabilityWindows = details.AvailabilityWindows.ToArray() };
        return ExecuteAvailabilityMutationAsync(listingId, user,
            (service, token) => service.UpdateListingDetailsAsync(listingId, user, snapshot, token), cancellationToken);
    }

    /// <summary>
    /// Applies validated scalar and optional window changes inside the caller's availability transaction when needed.
    /// </summary>
    private async Task<ListingServiceResult<Listing>> UpdateListingDetailsAsync(int listingId, ClaimsPrincipal? user,
        ListingUpdate? details, CancellationToken cancellationToken)
    {
        var owned = await GetOwnedListingAsync(listingId, user, cancellationToken);
        if (!owned.IsSuccess)
        {
            return owned;
        }

        var listing = owned.Value!;
        if (listing.Status == ListingStatus.Archived)
        {
            return ArchivedFailure();
        }

        if (details is null)
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.ValidationFailed,
                new ListingValidationError(string.Empty, SharedResource.Get("ListingValidation.ListingDetailsAreRequired")));
        }

        var windows = details.AvailabilityWindows?.ToArray();
        var candidate = new Listing { ListingId = listing.ListingId, OwnerId = listing.OwnerId, Status = listing.Status };
        ApplyEditableDetails(details, candidate);
        var errors = listing.Status == ListingStatus.Draft
            ? ValidateDraft(candidate, windows)
            : await GetPublicationErrorsAsync(candidate, windows, cancellationToken);
        if (errors.Count > 0)
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.ValidationFailed, errors.ToArray());
        }

        if (windows is not null)
        {
            var blocks = await context.AvailabilityBlocks.AsNoTracking()
                .Where(block => block.ListingId == listingId).ToListAsync(cancellationToken);
            var availability = await ValidateAvailabilityStateAsync(listingId, listing.Status, windows, blocks, cancellationToken);
            if (!availability.IsSuccess)
            {
                return ListingServiceResult<Listing>.Failure(availability.Status, availability.Errors.ToArray());
            }
        }

        ApplyEditableDetails(details, listing);
        if (windows is not null)
        {
            await ReplaceStoredWindowsAsync(listingId, windows, cancellationToken);
        }

        listing.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        return ListingServiceResult<Listing>.Success(listing);
    }

    /// <summary>
    /// Returns publication errors while leaving the listing and its timestamps unchanged.
    /// </summary>
    public async Task<ListingServiceResult<Listing>> ValidateForPublicationAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        var owned = await GetOwnedListingAsync(listingId, user, cancellationToken);
        if (!owned.IsSuccess)
        {
            return owned;
        }

        if (owned.Value!.Status == ListingStatus.Archived)
        {
            return ArchivedFailure();
        }

        var errors = await GetPublicationErrorsAsync(owned.Value, null, cancellationToken);
        return errors.Count == 0
            ? owned
            : ListingServiceResult<Listing>.Failure(ListingServiceStatus.ValidationFailed, errors.ToArray());
    }

    /// <summary>Validates the proposed review fields with the same stored images and availability used during publication, without writing them.</summary>
    public async Task<ListingServiceResult<bool>> ValidateProposedForPublicationAsync(int listingId, ClaimsPrincipal? user,
        ListingUpdate details, CancellationToken cancellationToken = default)
    {
        var owned = await GetOwnedListingAsync(listingId, user, cancellationToken);
        if (!owned.IsSuccess || owned.Value!.Status == ListingStatus.Archived)
        {
            return ListingServiceResult<bool>.Failure(owned.IsSuccess ? ListingServiceStatus.InvalidState : owned.Status,
                new ListingValidationError(string.Empty, owned.IsSuccess ? SharedResource.Get("ListingEditor.ArchivedHelp") : SharedResource.Get("ListingValidation.TheListingIsNotAvailableToThisAccount")));
        }
        var candidate = new Listing { ListingId = listingId, OwnerId = owned.Value.OwnerId, Status = owned.Value.Status };
        ApplyEditableDetails(details, candidate);
        var errors = await GetPublicationErrorsAsync(candidate, null, cancellationToken);
        return errors.Count == 0 ? ListingServiceResult<bool>.Success(true)
            : ListingServiceResult<bool>.Failure(ListingServiceStatus.ValidationFailed, errors.ToArray());
    }

    /// <summary>
    /// Publishes a valid listing, preserving its creation time and any original publication time.
    /// </summary>
    public Task<ListingServiceResult<Listing>> PublishAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default) =>
        ExecuteAvailabilityMutationAsync(listingId, user,
            (service, token) => service.PublishListingAsync(listingId, user, token), cancellationToken);

    /// <summary>
    /// Validates and publishes within the availability transaction, preserving the original publication timestamp.
    /// </summary>
    private async Task<ListingServiceResult<Listing>> PublishListingAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateForPublicationAsync(listingId, user, cancellationToken);
        if (!validated.IsSuccess || validated.Value!.Status == ListingStatus.Published)
        {
            return validated;
        }

        var listing = validated.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        listing.Status = ListingStatus.Published;
        listing.PublishedAtUtc ??= now;
        listing.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);
        return ListingServiceResult<Listing>.Success(listing);
    }

    /// <summary>
    /// Archives an owner's listing without removing rental, image, availability, or messaging records.
    /// </summary>
    public async Task<ListingServiceResult<Listing>> ArchiveAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        var owned = await GetOwnedListingAsync(listingId, user, cancellationToken);
        if (!owned.IsSuccess || owned.Value!.Status == ListingStatus.Archived)
        {
            return owned;
        }

        var listing = owned.Value;
        listing.Status = ListingStatus.Archived;
        listing.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        return ListingServiceResult<Listing>.Success(listing);
    }

    /// <summary>
    /// Retrieves detached owner listings with image and editable availability metadata in most-recently-updated order.
    /// </summary>
    public async Task<ListingServiceResult<IReadOnlyList<Listing>>> GetOwnerListingsAsync(ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        var ownerId = GetAuthenticatedOwnerId(user);
        if (ownerId is null)
        {
            return ListingServiceResult<IReadOnlyList<Listing>>.Failure(ListingServiceStatus.Unauthenticated,
                new ListingValidationError(nameof(Listing.OwnerId), SignInMessage));
        }

        var listings = await context.Listings.AsNoTracking()
            .Where(listing => listing.OwnerId == ownerId)
            .Include(listing => listing.Images)
            .Include(listing => listing.AvailabilityWindows)
            .Include(listing => listing.AvailabilityBlocks)
            .AsSplitQuery()
            .OrderByDescending(listing => listing.UpdatedAtUtc)
            .ThenByDescending(listing => listing.ListingId)
            .ToListAsync(cancellationToken);
        return ListingServiceResult<IReadOnlyList<Listing>>.Success(listings);
    }

    /// <summary>
    /// Searches only published listings, applying all filters and paging in the database.
    /// </summary>
    public async Task<ListingServiceResult<ListingSearchPage>> SearchPublishedAsync(ListingSearchParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters ??= new ListingSearchParameters();
        var errors = ValidateSearchParameters(parameters);
        if (errors.Count > 0)
        {
            return ListingServiceResult<ListingSearchPage>.Failure(ListingServiceStatus.ValidationFailed, errors.ToArray());
        }

        var query = BuildSearchQuery(parameters);
        var totalCount = await query.CountAsync(cancellationToken);
        var offset = (int)(((long)parameters.PageNumber - 1) * parameters.PageSize);
        var items = totalCount == 0
            ? new List<ListingSummary>()
            : await ProjectSummaries(query.Skip(offset).Take(parameters.PageSize)).ToListAsync(cancellationToken);
        return ListingServiceResult<ListingSearchPage>.Success(
            new ListingSearchPage(items, totalCount, parameters.PageNumber, parameters.PageSize));
    }

    private const int PUBLIC_ROW_LIMIT = 10;

    /// <summary>
    /// Uses newest-first selection until an actual popularity ranking is introduced.
    /// </summary>
    public async Task<IReadOnlyList<ListingSummary>> GetPopularListingsAsync(CancellationToken cancellationToken = default) =>
        await ProjectSummaries(BuildPublicRowQuery()).ToListAsync(cancellationToken);

    /// <summary>
    /// Returns same-category cards only when the source listing is itself public, without loading its entity.
    /// </summary>
    public async Task<IReadOnlyList<ListingSummary>> GetSimilarListingsAsync(int listingId, CancellationToken cancellationToken = default) =>
        await ProjectSummaries(BuildPublicRowQuery(listingId)).ToListAsync(cancellationToken);

    /// <summary>
    /// Bounds public rows in SQL with a stable ID tie-breaker and an optional same-category source predicate.
    /// </summary>
    private IQueryable<Listing> BuildPublicRowQuery(int? similarToId = null)
    {
        var query = PublishedListingsQuery();
        if (similarToId.HasValue)
        {
            var sources = PublishedListingsQuery();
            query = query.Where(listing => listing.ListingId != similarToId.Value && listing.Category != null
                && sources.Any(source => source.ListingId == similarToId.Value && source.Category == listing.Category));
        }
        return query.OrderByDescending(listing => listing.PublishedAtUtc ?? listing.CreatedAtUtc)
            .ThenByDescending(listing => listing.ListingId).Take(PUBLIC_ROW_LIMIT);
    }

    /// <summary>
    /// Retrieves published item details, ordered images, and date ranges using public-only projections.
    /// </summary>
    public async Task<ListingServiceResult<ListingDetails>> GetPublishedDetailsAsync(int listingId,
        CancellationToken cancellationToken = default)
    {
        if (listingId <= 0)
        {
            return ListingServiceResult<ListingDetails>.Failure(ListingServiceStatus.ValidationFailed,
                new ListingValidationError(nameof(Listing.ListingId), SharedResource.Get("ListingValidation.ListingIDMustBePositive")));
        }

        var details = await BuildDetailsQuery(listingId).SingleOrDefaultAsync(cancellationToken);
        return details is null
            ? ListingServiceResult<ListingDetails>.Failure(ListingServiceStatus.NotFound,
                new ListingValidationError(nameof(Listing.ListingId), SharedResource.Get("ListingValidation.TheListingCouldNotBeFound")))
            : ListingServiceResult<ListingDetails>.Success(details);
    }

    /// <summary>
    /// Establishes the read-only published scope shared by every public listing query.
    /// </summary>
    private IQueryable<Listing> PublishedListingsQuery() =>
        context.Listings.AsNoTracking().Where(listing => listing.Status == ListingStatus.Published);

    /// <summary>
    /// Builds translatable substring and scalar filters with a unique ordering for reliable pagination.
    /// </summary>
    private IOrderedQueryable<Listing> BuildSearchQuery(ListingSearchParameters parameters)
    {
        var query = PublishedListingsQuery();
        foreach (var term in GetSearchTerms(parameters.SearchText))
        {
            query = query.Where(listing => (listing.Title != null && listing.Title.Contains(term))
                || (listing.Description != null && listing.Description.Contains(term)));
        }
        if (parameters.Category.HasValue)
        {
            query = query.Where(listing => listing.Category == parameters.Category.Value);
        }
        if (parameters.Condition.HasValue)
        {
            query = query.Where(listing => listing.Condition == parameters.Condition.Value);
        }
        if (parameters.MinimumDailyPriceCents.HasValue)
        {
            query = query.Where(listing => listing.DailyPriceCents >= parameters.MinimumDailyPriceCents.Value);
        }
        if (parameters.MaximumDailyPriceCents.HasValue)
        {
            query = query.Where(listing => listing.DailyPriceCents <= parameters.MaximumDailyPriceCents.Value);
        }
        var pickupArea = parameters.PickupArea?.Trim();
        if (!string.IsNullOrEmpty(pickupArea))
        {
            query = query.Where(listing => listing.PickupArea != null && listing.PickupArea.Contains(pickupArea));
        }

        return parameters.SortOrder switch
        {
            ListingSortOrder.PriceLowToHigh => query.OrderBy(listing => listing.DailyPriceCents).ThenBy(listing => listing.ListingId),
            ListingSortOrder.PriceHighToLow => query.OrderByDescending(listing => listing.DailyPriceCents).ThenBy(listing => listing.ListingId),
            _ => query.OrderByDescending(listing => listing.PublishedAtUtc ?? listing.CreatedAtUtc).ThenByDescending(listing => listing.ListingId)
        };
    }

    /// <summary>
    /// Projects bounded result summaries and ordered gallery references without loading listing or owner entities.
    /// </summary>
    private static IQueryable<ListingSummary> ProjectSummaries(IQueryable<Listing> query) =>
        query.Select(listing => new ListingSummary(
            listing.ListingId, listing.Title, listing.Category, listing.Condition, listing.DailyPriceCents!.Value,
            listing.PickupArea, listing.CreatedAtUtc, listing.PublishedAtUtc,
            listing.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.DisplayOrder)
                .ThenBy(image => image.ListingImageId)
                .Select(image => new ListingImageInfo(image.ListingImageId, image.ContainerName, image.BlobName,
                    image.ContentType, image.AltText, image.DisplayOrder, image.IsPrimary)).FirstOrDefault())
            {
                Images = listing.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.DisplayOrder)
                    .ThenBy(image => image.ListingImageId)
                    .Select(image => new ListingThumbnailInfo(image.ListingImageId, image.AltText, image.DisplayOrder, image.IsPrimary)).ToList()
            });

    /// <summary>
    /// Projects one published listing and its public child data without loading an Identity entity or private notes.
    /// </summary>
    private IQueryable<ListingDetails> BuildDetailsQuery(int listingId) =>
        PublishedListingsQuery().Where(listing => listing.ListingId == listingId).AsSplitQuery()
            .Select(listing => new ListingDetails(
                listing.ListingId, listing.Title, listing.Description, listing.Category, listing.Condition,
                listing.DailyPriceCents!.Value, listing.PickupArea, listing.Latitude, listing.Longitude,
                listing.CreatedAtUtc, listing.UpdatedAtUtc, listing.PublishedAtUtc, listing.Owner.DisplayName,
                listing.Images.OrderBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId)
                    .Select(image => new ListingImageInfo(image.ListingImageId, image.ContainerName, image.BlobName,
                        image.ContentType, image.AltText, image.DisplayOrder, image.IsPrimary)).ToList(),
                listing.AvailabilityWindows.OrderBy(window => window.StartDate).ThenBy(window => window.EndDate)
                    .ThenBy(window => window.AvailabilityWindowId)
                    .Select(window => new ListingAvailabilityPeriod(window.StartDate, window.EndDate)).ToList(),
                listing.AvailabilityBlocks.OrderBy(block => block.StartDate).ThenBy(block => block.EndDate)
                    .ThenBy(block => block.AvailabilityBlockId)
                    .Select(block => new ListingAvailabilityPeriod(block.StartDate, block.EndDate)).ToList()));

    /// <summary>
    /// Validates enum filters, monetary bounds, text limits, and safe bounded pagination before querying.
    /// </summary>
    private static List<ListingValidationError> ValidateSearchParameters(ListingSearchParameters parameters)
    {
        var errors = new List<ListingValidationError>();
        if (parameters.Category.HasValue && !Enum.IsDefined(parameters.Category.Value))
        {
            errors.Add(new(nameof(parameters.Category), SharedResource.Get("Validation.InvalidCategory")));
        }
        if (parameters.Condition.HasValue && !Enum.IsDefined(parameters.Condition.Value))
        {
            errors.Add(new(nameof(parameters.Condition), SharedResource.Get("Validation.InvalidCondition")));
        }
        if (!Enum.IsDefined(parameters.SortOrder))
        {
            errors.Add(new(nameof(parameters.SortOrder), SharedResource.Get("Validation.InvalidSort")));
        }
        if (parameters.MinimumDailyPriceCents < 0)
        {
            errors.Add(new(nameof(parameters.MinimumDailyPriceCents), SharedResource.Get("Validation.MinimumPrice")));
        }
        if (parameters.MaximumDailyPriceCents < 0)
        {
            errors.Add(new(nameof(parameters.MaximumDailyPriceCents), SharedResource.Get("Validation.MaximumPrice")));
        }
        if (parameters.MinimumDailyPriceCents > parameters.MaximumDailyPriceCents)
        {
            errors.Add(new(nameof(parameters.MaximumDailyPriceCents), SharedResource.Get("Validation.PriceRange")));
        }
        if (parameters.PageNumber < 1)
        {
            errors.Add(new(nameof(parameters.PageNumber), SharedResource.Get("Validation.PageNumber")));
        }
        if (parameters.PageSize < 1 || parameters.PageSize > ListingSearchParameters.MAX_PAGE_SIZE)
        {
            errors.Add(new(nameof(parameters.PageSize), SharedResource.Get("Validation.PageSize", ListingSearchParameters.MAX_PAGE_SIZE)));
        }
        if (parameters.PageNumber > 0 && parameters.PageSize > 0
            && ((long)parameters.PageNumber - 1) * parameters.PageSize > int.MaxValue)
        {
            errors.Add(new(nameof(parameters.PageNumber), SharedResource.Get("Validation.PageRange")));
        }
        if (parameters.SearchText?.Length > ListingSearchParameters.MAX_SEARCH_TEXT_LENGTH)
        {
            errors.Add(new(nameof(parameters.SearchText), SharedResource.Get("Validation.SearchLength", ListingSearchParameters.MAX_SEARCH_TEXT_LENGTH)));
        }
        else if (GetSearchTerms(parameters.SearchText).Length > ListingSearchParameters.MAX_SEARCH_TERMS)
        {
            errors.Add(new(nameof(parameters.SearchText), SharedResource.Get("Validation.SearchTerms", ListingSearchParameters.MAX_SEARCH_TERMS)));
        }
        if (parameters.PickupArea?.Length > ListingSearchParameters.MAX_PICKUP_AREA_LENGTH)
        {
            errors.Add(new(nameof(parameters.PickupArea), SharedResource.Get("Validation.PickupLength", ListingSearchParameters.MAX_PICKUP_AREA_LENGTH)));
        }
        return errors;
    }

    /// <summary>
    /// Extracts distinct literal search terms without introducing a query language or wildcard syntax.
    /// </summary>
    private static string[] GetSearchTerms(string? searchText) =>
        (searchText ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Loads a listing only after authenticating the caller and verifies immutable ownership.
    /// </summary>
    private Task<ListingServiceResult<Listing>> GetOwnedListingAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken) => ListingOperationScope.GetOwnedListingAsync(context, listingId, user, cancellationToken);

    /// <summary>
    /// Collects all required field, stored image, and availability errors before publication.
    /// </summary>
    private async Task<List<ListingValidationError>> GetPublicationErrorsAsync(Listing listing,
        IReadOnlyList<ListingAvailabilityPeriod>? proposedWindows, CancellationToken cancellationToken)
    {
        var errors = ValidateDraft(listing, null);
        if (string.IsNullOrWhiteSpace(listing.Title))
        {
            errors.Add(new(nameof(Listing.Title), SharedResource.Get("ListingValidation.EnterAnItemTitleBeforePublishing")));
        }
        if (string.IsNullOrWhiteSpace(listing.Description))
        {
            errors.Add(new(nameof(Listing.Description), SharedResource.Get("ListingValidation.EnterAnItemDescriptionBeforePublishing")));
        }
        if (!listing.Category.HasValue)
        {
            errors.Add(new(nameof(Listing.Category), SharedResource.Get("ListingValidation.SelectAnItemCategoryBeforePublishing")));
        }
        if (!listing.Condition.HasValue)
        {
            errors.Add(new(nameof(Listing.Condition), SharedResource.Get("ListingValidation.SelectTheItemSConditionBeforePublishing")));
        }
        if (!listing.DailyPriceCents.HasValue || listing.DailyPriceCents == 0)
        {
            errors.Add(new(nameof(Listing.DailyPriceCents), SharedResource.Get("ListingValidation.EnterADailyPriceGreaterThanCA0Before")));
        }
        if (string.IsNullOrWhiteSpace(listing.PickupArea))
        {
            errors.Add(new(nameof(Listing.PickupArea), SharedResource.Get("ListingValidation.EnterAPickupAreaBeforePublishing")));
        }

        var images = await context.ListingImages.AsNoTracking()
            .Where(image => image.ListingId == listing.ListingId).ToListAsync(cancellationToken);
        if (!images.Any(HasStoredImageMetadata))
        {
            errors.Add(new(nameof(Listing.Images), SharedResource.Get("ListingValidation.AddAtLeastOneStoredListingImageBeforePublishing")));
        }

        var windows = proposedWindows ?? await context.AvailabilityWindows.AsNoTracking()
            .Where(window => window.ListingId == listing.ListingId)
            .Select(window => new ListingAvailabilityPeriod(window.StartDate, window.EndDate))
            .ToListAsync(cancellationToken);
        if (windows.Count == 0)
        {
            errors.Add(new(nameof(Listing.AvailabilityWindows), SharedResource.Get("ListingValidation.AddAtLeastOneAvailabilityWindowBeforePublishing")));
        }
        errors.AddRange(ValidateAvailabilityWindows(windows));
        return errors;
    }

    /// <summary>
    /// Validates supplied values against model limits while allowing incomplete drafts.
    /// </summary>
    private static List<ListingValidationError> ValidateDraft(Listing listing,
        IReadOnlyList<ListingAvailabilityPeriod>? windows)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(listing, new ValidationContext(listing), results, validateAllProperties: true);
        var errors = results.SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
            .Select(field => new ListingValidationError(field, ResourceValidation.Message(listing, field)))).ToList();
        if (listing.Category.HasValue && !Enum.IsDefined(listing.Category.Value))
        {
            errors.Add(new(nameof(Listing.Category), SharedResource.Get("Validation.InvalidCategory")));
        }
        if (listing.Condition.HasValue && !Enum.IsDefined(listing.Condition.Value))
        {
            errors.Add(new(nameof(Listing.Condition), SharedResource.Get("Validation.InvalidCondition")));
        }
        if (listing.DailyPriceCents < 0)
        {
            errors.Add(new(nameof(Listing.DailyPriceCents), SharedResource.Get("ListingValidation.DailyPriceCannotBeNegative")));
        }
        if (listing.Latitude.HasValue && (!double.IsFinite(listing.Latitude.Value) || listing.Latitude < -90 || listing.Latitude > 90))
        {
            errors.Add(new(nameof(Listing.Latitude), SharedResource.Get("ListingValidation.LatitudeMustBeBetween90And90")));
        }
        if (listing.Longitude.HasValue && (!double.IsFinite(listing.Longitude.Value) || listing.Longitude < -180 || listing.Longitude > 180))
        {
            errors.Add(new(nameof(Listing.Longitude), SharedResource.Get("ListingValidation.LongitudeMustBeBetween180And180")));
        }
        if (windows is not null)
        {
            errors.AddRange(ValidateAvailabilityWindows(windows));
        }
        return errors;
    }

    /// <summary>
    /// Rejects invalid or exact duplicate ranges while allowing adjacent and overlapping windows.
    /// </summary>
    private static IEnumerable<ListingValidationError> ValidateAvailabilityWindows(IReadOnlyList<ListingAvailabilityPeriod> windows)
    {
        var seen = new HashSet<ListingAvailabilityPeriod>();
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            if (window is null || window.StartDate == default || window.EndDate == default || window.StartDate >= window.EndDate)
            {
                yield return new($"{nameof(Listing.AvailabilityWindows)}[{index}]", SharedResource.Get("ListingValidation.ChooseAvailabilityDatesWithTheStartBeforeTheEnd"));
            }
            else if (!seen.Add(window))
            {
                yield return new($"{nameof(Listing.AvailabilityWindows)}[{index}]", SharedResource.Get("ListingValidation.AnAvailabilityWindowWithTheseDatesAlreadyExists"));
            }
        }
    }

    /// <summary>
    /// Copies only editable details, converting blank optional text into unfinished draft fields.
    /// </summary>
    private static void ApplyEditableDetails(ListingUpdate details, Listing listing)
    {
        listing.Title = string.IsNullOrWhiteSpace(details.Title) ? null : details.Title.Trim();
        listing.Description = string.IsNullOrWhiteSpace(details.Description) ? null : details.Description.Trim();
        listing.Category = details.Category;
        listing.Condition = details.Condition;
        listing.DailyPriceCents = details.DailyPriceCents;
        listing.PickupArea = string.IsNullOrWhiteSpace(details.PickupArea) ? null : details.PickupArea.Trim();
        listing.Latitude = details.Latitude;
        listing.Longitude = details.Longitude;
    }

    /// <summary>
    /// Reads the owner identifier only from an authenticated Identity claims identity.
    /// </summary>
    private static string? GetAuthenticatedOwnerId(ClaimsPrincipal? user) => ListingOperationScope.GetOwnerId(user);

    /// <summary>
    /// Identifies stored image metadata that satisfies the existing listing publication requirement.
    /// </summary>
    internal static bool HasStoredImageMetadata(ListingImage image) => !string.IsNullOrWhiteSpace(image.ContainerName)
        && !string.IsNullOrWhiteSpace(image.BlobName) && image.SizeBytes > 0
        && image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Explains why an archived listing cannot be edited or republished.
    /// </summary>
    private static ListingServiceResult<Listing> ArchivedFailure() =>
        ListingServiceResult<Listing>.Failure(ListingServiceStatus.InvalidState,
            new ListingValidationError(nameof(Listing.Status), SharedResource.Get("ListingValidation.ArchivedListingsCannotBeEditedOrPublished")));
}
