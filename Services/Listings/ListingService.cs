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
    private const string SIGN_IN_MESSAGE = "Sign in to manage your listings.";
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

    /// <summary>
    /// Creates an empty draft associated with the authenticated, existing owner.
    /// </summary>
    public async Task<ListingServiceResult<Listing>> CreateDraftAsync(ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        var ownerId = GetAuthenticatedOwnerId(user);
        if (ownerId is null)
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.Unauthenticated,
                new ListingValidationError(nameof(Listing.OwnerId), SIGN_IN_MESSAGE));
        }

        if (!await context.Users.AnyAsync(owner => owner.Id == ownerId, cancellationToken))
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.NotFound,
                new ListingValidationError(nameof(Listing.OwnerId), "Your account could not be found."));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var listing = new Listing
        {
            OwnerId = ownerId,
            Status = ListingStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.Listings.Add(listing);
        await context.SaveChangesAsync(cancellationToken);
        return ListingServiceResult<Listing>.Success(listing);
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
                new ListingValidationError(string.Empty, "Listing details are required."));
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
            var storedWindows = await context.AvailabilityWindows
                .Where(window => window.ListingId == listingId).ToListAsync(cancellationToken);
            context.AvailabilityWindows.RemoveRange(storedWindows);
            context.AvailabilityWindows.AddRange(windows.Select(window => new AvailabilityWindow
            {
                ListingId = listingId,
                StartDate = window.StartDate,
                EndDate = window.EndDate
            }));
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
                new ListingValidationError(nameof(Listing.OwnerId), SIGN_IN_MESSAGE));
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

    /// <summary>
    /// Retrieves published item details, ordered images, and date ranges using public-only projections.
    /// </summary>
    public async Task<ListingServiceResult<ListingDetails>> GetPublishedDetailsAsync(int listingId,
        CancellationToken cancellationToken = default)
    {
        if (listingId <= 0)
        {
            return ListingServiceResult<ListingDetails>.Failure(ListingServiceStatus.ValidationFailed,
                new ListingValidationError(nameof(Listing.ListingId), "Listing ID must be positive."));
        }

        var details = await BuildDetailsQuery(listingId).SingleOrDefaultAsync(cancellationToken);
        return details is null
            ? ListingServiceResult<ListingDetails>.Failure(ListingServiceStatus.NotFound,
                new ListingValidationError(nameof(Listing.ListingId), "The listing could not be found."))
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
    /// Projects only public summary fields and the primary image, falling back to the first displayed image.
    /// </summary>
    private static IQueryable<ListingSummary> ProjectSummaries(IQueryable<Listing> query) =>
        query.Select(listing => new ListingSummary(
            listing.ListingId, listing.Title, listing.Category, listing.Condition, listing.DailyPriceCents!.Value,
            listing.PickupArea, listing.CreatedAtUtc, listing.PublishedAtUtc,
            listing.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.DisplayOrder)
                .ThenBy(image => image.ListingImageId)
                .Select(image => new ListingImageInfo(image.ListingImageId, image.ContainerName, image.BlobName,
                    image.ContentType, image.AltText, image.DisplayOrder, image.IsPrimary)).FirstOrDefault()));

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
            errors.Add(new(nameof(parameters.Category), "Select a valid item category."));
        }
        if (parameters.Condition.HasValue && !Enum.IsDefined(parameters.Condition.Value))
        {
            errors.Add(new(nameof(parameters.Condition), "Select a valid item condition."));
        }
        if (!Enum.IsDefined(parameters.SortOrder))
        {
            errors.Add(new(nameof(parameters.SortOrder), "Select a valid listing sort order."));
        }
        if (parameters.MinimumDailyPriceCents < 0)
        {
            errors.Add(new(nameof(parameters.MinimumDailyPriceCents), "Minimum daily price cannot be negative."));
        }
        if (parameters.MaximumDailyPriceCents < 0)
        {
            errors.Add(new(nameof(parameters.MaximumDailyPriceCents), "Maximum daily price cannot be negative."));
        }
        if (parameters.MinimumDailyPriceCents > parameters.MaximumDailyPriceCents)
        {
            errors.Add(new(nameof(parameters.MaximumDailyPriceCents), "Maximum daily price must be at least the minimum."));
        }
        if (parameters.PageNumber < 1)
        {
            errors.Add(new(nameof(parameters.PageNumber), "Page number must be at least 1."));
        }
        if (parameters.PageSize < 1 || parameters.PageSize > ListingSearchParameters.MAX_PAGE_SIZE)
        {
            errors.Add(new(nameof(parameters.PageSize), $"Page size must be between 1 and {ListingSearchParameters.MAX_PAGE_SIZE}."));
        }
        if (parameters.PageNumber > 0 && parameters.PageSize > 0
            && ((long)parameters.PageNumber - 1) * parameters.PageSize > int.MaxValue)
        {
            errors.Add(new(nameof(parameters.PageNumber), "The requested page exceeds the supported pagination range."));
        }
        if (parameters.SearchText?.Length > ListingSearchParameters.MAX_SEARCH_TEXT_LENGTH)
        {
            errors.Add(new(nameof(parameters.SearchText), $"Search text cannot exceed {ListingSearchParameters.MAX_SEARCH_TEXT_LENGTH} characters."));
        }
        else if (GetSearchTerms(parameters.SearchText).Length > ListingSearchParameters.MAX_SEARCH_TERMS)
        {
            errors.Add(new(nameof(parameters.SearchText), $"Search supports at most {ListingSearchParameters.MAX_SEARCH_TERMS} distinct terms."));
        }
        if (parameters.PickupArea?.Length > ListingSearchParameters.MAX_PICKUP_AREA_LENGTH)
        {
            errors.Add(new(nameof(parameters.PickupArea), $"Pickup area cannot exceed {ListingSearchParameters.MAX_PICKUP_AREA_LENGTH} characters."));
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
    private async Task<ListingServiceResult<Listing>> GetOwnedListingAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken)
    {
        var ownerId = GetAuthenticatedOwnerId(user);
        if (ownerId is null)
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.Unauthenticated,
                new ListingValidationError(nameof(Listing.OwnerId), SIGN_IN_MESSAGE));
        }

        var listing = await context.Listings.SingleOrDefaultAsync(item => item.ListingId == listingId, cancellationToken);
        if (listing is null)
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.NotFound,
                new ListingValidationError(nameof(Listing.ListingId), "The listing could not be found."));
        }

        if (!string.Equals(listing.OwnerId, ownerId, StringComparison.Ordinal))
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.Forbidden,
                new ListingValidationError(nameof(Listing.OwnerId), "Only the listing owner can perform this action."));
        }

        return ListingServiceResult<Listing>.Success(listing);
    }

    /// <summary>
    /// Collects all required field, stored image, and availability errors before publication.
    /// </summary>
    private async Task<List<ListingValidationError>> GetPublicationErrorsAsync(Listing listing,
        IReadOnlyList<ListingAvailabilityPeriod>? proposedWindows, CancellationToken cancellationToken)
    {
        var errors = ValidateDraft(listing, null);
        if (string.IsNullOrWhiteSpace(listing.Title))
        {
            errors.Add(new(nameof(Listing.Title), "Enter an item title before publishing."));
        }
        if (string.IsNullOrWhiteSpace(listing.Description))
        {
            errors.Add(new(nameof(Listing.Description), "Enter an item description before publishing."));
        }
        if (!listing.Category.HasValue)
        {
            errors.Add(new(nameof(Listing.Category), "Select an item category before publishing."));
        }
        if (!listing.Condition.HasValue)
        {
            errors.Add(new(nameof(Listing.Condition), "Select the item's condition before publishing."));
        }
        if (!listing.DailyPriceCents.HasValue || listing.DailyPriceCents == 0)
        {
            errors.Add(new(nameof(Listing.DailyPriceCents), "Enter a daily price greater than CA$0 before publishing."));
        }
        if (string.IsNullOrWhiteSpace(listing.PickupArea))
        {
            errors.Add(new(nameof(Listing.PickupArea), "Enter a pickup area before publishing."));
        }

        var images = await context.ListingImages.AsNoTracking()
            .Where(image => image.ListingId == listing.ListingId).ToListAsync(cancellationToken);
        if (!images.Any(image => !string.IsNullOrWhiteSpace(image.ContainerName)
            && !string.IsNullOrWhiteSpace(image.BlobName) && image.SizeBytes > 0
            && image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(new(nameof(Listing.Images), "Add at least one stored listing image before publishing."));
        }

        var windows = proposedWindows ?? await context.AvailabilityWindows.AsNoTracking()
            .Where(window => window.ListingId == listing.ListingId)
            .Select(window => new ListingAvailabilityPeriod(window.StartDate, window.EndDate))
            .ToListAsync(cancellationToken);
        if (windows.Count == 0)
        {
            errors.Add(new(nameof(Listing.AvailabilityWindows), "Add at least one availability window before publishing."));
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
            .Select(field => new ListingValidationError(field, result.ErrorMessage ?? "The value is invalid."))).ToList();
        if (listing.Category.HasValue && !Enum.IsDefined(listing.Category.Value))
        {
            errors.Add(new(nameof(Listing.Category), "Select a valid item category."));
        }
        if (listing.Condition.HasValue && !Enum.IsDefined(listing.Condition.Value))
        {
            errors.Add(new(nameof(Listing.Condition), "Select a valid item condition."));
        }
        if (listing.DailyPriceCents < 0)
        {
            errors.Add(new(nameof(Listing.DailyPriceCents), "Daily price cannot be negative."));
        }
        if (listing.Latitude.HasValue && (!double.IsFinite(listing.Latitude.Value) || listing.Latitude < -90 || listing.Latitude > 90))
        {
            errors.Add(new(nameof(Listing.Latitude), "Latitude must be between -90 and 90."));
        }
        if (listing.Longitude.HasValue && (!double.IsFinite(listing.Longitude.Value) || listing.Longitude < -180 || listing.Longitude > 180))
        {
            errors.Add(new(nameof(Listing.Longitude), "Longitude must be between -180 and 180."));
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
                yield return new($"{nameof(Listing.AvailabilityWindows)}[{index}]", "Choose availability dates with the start before the end.");
            }
            else if (!seen.Add(window))
            {
                yield return new($"{nameof(Listing.AvailabilityWindows)}[{index}]", "An availability window with these dates already exists.");
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
    private static string? GetAuthenticatedOwnerId(ClaimsPrincipal? user)
    {
        var ownerId = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)
            ?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(ownerId) ? null : ownerId;
    }

    /// <summary>
    /// Explains why an archived listing cannot be edited or republished.
    /// </summary>
    private static ListingServiceResult<Listing> ArchivedFailure() =>
        ListingServiceResult<Listing>.Failure(ListingServiceStatus.InvalidState,
            new ListingValidationError(nameof(Listing.Status), "Archived listings cannot be edited or published."));
}
