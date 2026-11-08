using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RentRoad.Data;
using RentRoad.Models;
using RentRoad.Models.Enums;

namespace RentRoad.Services.Listings;

/// <summary>
/// Manages owner availability using shared listing authorization and reservation-preserving date rules.
/// </summary>
public sealed partial class ListingService
{
    /// <summary>
    /// Creates a valid, nonduplicate availability window for the authenticated listing owner.
    /// </summary>
    public Task<ListingServiceResult<AvailabilityWindow>> CreateAvailabilityWindowAsync(int listingId, ClaimsPrincipal? user,
        ListingAvailabilityPeriod? period, CancellationToken cancellationToken = default) =>
        ExecuteAvailabilityMutationAsync(listingId, user,
            (service, token) => service.ChangeAvailabilityWindowAsync(listingId, null, user, period, remove: false, token), cancellationToken);

    /// <summary>
    /// Updates a window belonging to the selected listing without invalidating protected reservations.
    /// </summary>
    public Task<ListingServiceResult<AvailabilityWindow>> UpdateAvailabilityWindowAsync(int listingId, int windowId,
        ClaimsPrincipal? user, ListingAvailabilityPeriod? period, CancellationToken cancellationToken = default) =>
        ExecuteAvailabilityMutationAsync(listingId, user,
            (service, token) => service.ChangeAvailabilityWindowAsync(listingId, windowId, user, period, remove: false, token), cancellationToken);

    /// <summary>
    /// Removes a selected listing's window while preserving required coverage and publication consistency.
    /// </summary>
    public Task<ListingServiceResult<AvailabilityWindow>> RemoveAvailabilityWindowAsync(int listingId, int windowId,
        ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        ExecuteAvailabilityMutationAsync(listingId, user,
            (service, token) => service.ChangeAvailabilityWindowAsync(listingId, windowId, user, null, remove: true, token), cancellationToken);

    /// <summary>
    /// Creates an unavailable period and optional private reason for the authenticated owner.
    /// </summary>
    public Task<ListingServiceResult<AvailabilityBlock>> CreateAvailabilityBlockAsync(int listingId, ClaimsPrincipal? user,
        ListingAvailabilityBlockUpdate? details, CancellationToken cancellationToken = default) =>
        ExecuteAvailabilityMutationAsync(listingId, user,
            (service, token) => service.ChangeAvailabilityBlockAsync(listingId, null, user, details, remove: false, token), cancellationToken);

    /// <summary>
    /// Updates a selected listing's block without overlapping approved or active reservations.
    /// </summary>
    public Task<ListingServiceResult<AvailabilityBlock>> UpdateAvailabilityBlockAsync(int listingId, int blockId,
        ClaimsPrincipal? user, ListingAvailabilityBlockUpdate? details, CancellationToken cancellationToken = default) =>
        ExecuteAvailabilityMutationAsync(listingId, user,
            (service, token) => service.ChangeAvailabilityBlockAsync(listingId, blockId, user, details, remove: false, token), cancellationToken);

    /// <summary>
    /// Removes an owner's unavailable period while retaining the listing and rental history.
    /// </summary>
    public Task<ListingServiceResult<AvailabilityBlock>> RemoveAvailabilityBlockAsync(int listingId, int blockId,
        ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        ExecuteAvailabilityMutationAsync(listingId, user,
            (service, token) => service.ChangeAvailabilityBlockAsync(listingId, blockId, user, null, remove: true, token), cancellationToken);

    /// <summary>
    /// Serializes availability checks and writes, recreating a context with the same provider and model for each retry.
    /// </summary>
    /// <remarks>
    /// A separate change tracker prevents failed retries from leaking mutations into the scoped context.
    /// Successful writes invalidate only unchanged tracked listing/availability records for this listing.
    /// Callers must save or discard pending changes before starting an independent availability unit of work.
    /// </remarks>
    private async Task<ListingServiceResult<T>> ExecuteAvailabilityMutationAsync<T>(int listingId, ClaimsPrincipal? user,
        Func<ListingService, CancellationToken, Task<ListingServiceResult<T>>> operation, CancellationToken cancellationToken)
    {
        if (GetAuthenticatedOwnerId(user) is null)
        {
            return ListingServiceResult<T>.Failure(ListingServiceStatus.Unauthenticated,
                new ListingValidationError(nameof(Listing.OwnerId), SIGN_IN_MESSAGE));
        }
        var tracked = context.ChangeTracker.Entries().Where(entry =>
            entry.Entity is Listing listing && listing.ListingId == listingId
            || entry.Entity is AvailabilityWindow window && window.ListingId == listingId
            || entry.Entity is AvailabilityBlock block && block.ListingId == listingId).ToArray();
        if (context.Database.CurrentTransaction is not null || tracked.Any(entry => entry.State != EntityState.Unchanged))
        {
            return ListingServiceResult<T>.Failure(ListingServiceStatus.Conflict,
                new ListingValidationError(nameof(Listing.ListingId), "Save or discard pending listing changes and complete the current transaction before modifying availability or publishing."));
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>(
            (DbContextOptions<ApplicationDbContext>)context.GetService<IDbContextOptions>()).UseModel(context.Model).Options;
        var result = await context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var writeContext = new ApplicationDbContext(options);
            await using var transaction = await writeContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var outcome = await operation(new ListingService(writeContext, timeProvider), token);
            if (outcome.IsSuccess)
            {
                await transaction.CommitAsync(token);
            }
            return outcome;
        }, cancellationToken);
        if (result.IsSuccess)
        {
            foreach (var entry in tracked)
            {
                entry.State = EntityState.Detached;
            }
        }
        return result;
    }

    /// <summary>
    /// Validates a proposed window set before applying a single atomic availability and timestamp save.
    /// </summary>
    private async Task<ListingServiceResult<AvailabilityWindow>> ChangeAvailabilityWindowAsync(int listingId, int? windowId,
        ClaimsPrincipal? user, ListingAvailabilityPeriod? period, bool remove, CancellationToken cancellationToken)
    {
        var owned = await GetEditableAvailabilityListingAsync(listingId, user, cancellationToken);
        if (!owned.IsSuccess)
        {
            return ListingServiceResult<AvailabilityWindow>.Failure(owned.Status, owned.Errors.ToArray());
        }
        if (windowId <= 0)
        {
            return ListingServiceResult<AvailabilityWindow>.Failure(ListingServiceStatus.ValidationFailed,
                new ListingValidationError(nameof(AvailabilityWindow.AvailabilityWindowId), "Window ID must be positive."));
        }
        if (!remove && period is null)
        {
            return ListingServiceResult<AvailabilityWindow>.Failure(ListingServiceStatus.ValidationFailed,
                new ListingValidationError(nameof(Listing.AvailabilityWindows), "Availability dates are required."));
        }

        var stored = await context.AvailabilityWindows.Where(window => window.ListingId == listingId)
            .ToListAsync(cancellationToken);
        var existing = windowId.HasValue ? stored.SingleOrDefault(window => window.AvailabilityWindowId == windowId) : null;
        if (windowId.HasValue && existing is null)
        {
            return ListingServiceResult<AvailabilityWindow>.Failure(ListingServiceStatus.NotFound,
                new ListingValidationError(nameof(AvailabilityWindow.AvailabilityWindowId), "The availability window could not be found for this listing."));
        }

        var proposed = stored.Where(window => window.AvailabilityWindowId != windowId)
            .Select(window => new ListingAvailabilityPeriod(window.StartDate, window.EndDate)).ToList();
        if (!remove)
        {
            proposed.Add(period!);
        }
        var blocks = await context.AvailabilityBlocks.AsNoTracking().Where(block => block.ListingId == listingId)
            .ToListAsync(cancellationToken);
        var validation = await ValidateAvailabilityStateAsync(listingId, owned.Value!.Status, proposed, blocks, cancellationToken);
        if (!validation.IsSuccess)
        {
            return ListingServiceResult<AvailabilityWindow>.Failure(validation.Status, validation.Errors.ToArray());
        }

        var changed = existing ?? new AvailabilityWindow { ListingId = listingId };
        if (remove)
        {
            context.AvailabilityWindows.Remove(changed);
        }
        else
        {
            changed.StartDate = period!.StartDate;
            changed.EndDate = period.EndDate;
            if (existing is null)
            {
                context.AvailabilityWindows.Add(changed);
            }
        }
        owned.Value.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        return ListingServiceResult<AvailabilityWindow>.Success(changed);
    }

    /// <summary>
    /// Validates a proposed exception set before changing the stored block or its parent timestamp.
    /// </summary>
    private async Task<ListingServiceResult<AvailabilityBlock>> ChangeAvailabilityBlockAsync(int listingId, int? blockId,
        ClaimsPrincipal? user, ListingAvailabilityBlockUpdate? details, bool remove, CancellationToken cancellationToken)
    {
        var owned = await GetEditableAvailabilityListingAsync(listingId, user, cancellationToken);
        if (!owned.IsSuccess)
        {
            return ListingServiceResult<AvailabilityBlock>.Failure(owned.Status, owned.Errors.ToArray());
        }
        if (blockId <= 0)
        {
            return ListingServiceResult<AvailabilityBlock>.Failure(ListingServiceStatus.ValidationFailed,
                new ListingValidationError(nameof(AvailabilityBlock.AvailabilityBlockId), "Block ID must be positive."));
        }
        if (!remove && details is null)
        {
            return ListingServiceResult<AvailabilityBlock>.Failure(ListingServiceStatus.ValidationFailed,
                new ListingValidationError(nameof(Listing.AvailabilityBlocks), "Unavailable dates are required."));
        }

        var stored = await context.AvailabilityBlocks.Where(block => block.ListingId == listingId).ToListAsync(cancellationToken);
        var existing = blockId.HasValue ? stored.SingleOrDefault(block => block.AvailabilityBlockId == blockId) : null;
        if (blockId.HasValue && existing is null)
        {
            return ListingServiceResult<AvailabilityBlock>.Failure(ListingServiceStatus.NotFound,
                new ListingValidationError(nameof(AvailabilityBlock.AvailabilityBlockId), "The availability block could not be found for this listing."));
        }

        var changed = remove ? existing! : new AvailabilityBlock
        {
            AvailabilityBlockId = blockId ?? 0, ListingId = listingId,
            StartDate = details!.StartDate, EndDate = details.EndDate,
            Reason = string.IsNullOrWhiteSpace(details.Reason) ? null : details.Reason.Trim()
        };
        var proposed = stored.Where(block => block.AvailabilityBlockId != blockId).ToList();
        if (!remove)
        {
            proposed.Add(changed);
        }
        var windows = await context.AvailabilityWindows.AsNoTracking().Where(window => window.ListingId == listingId)
            .Select(window => new ListingAvailabilityPeriod(window.StartDate, window.EndDate)).ToListAsync(cancellationToken);
        var validation = await ValidateAvailabilityStateAsync(listingId, owned.Value!.Status, windows, proposed, cancellationToken);
        if (!validation.IsSuccess)
        {
            return ListingServiceResult<AvailabilityBlock>.Failure(validation.Status, validation.Errors.ToArray());
        }

        if (remove)
        {
            context.AvailabilityBlocks.Remove(changed);
        }
        else if (existing is null)
        {
            context.AvailabilityBlocks.Add(changed);
        }
        else
        {
            existing.StartDate = changed.StartDate;
            existing.EndDate = changed.EndDate;
            existing.Reason = changed.Reason;
            changed = existing;
        }
        owned.Value.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        return ListingServiceResult<AvailabilityBlock>.Success(changed);
    }

    /// <summary>
    /// Reuses listing ownership checks and prevents availability edits to archived listings.
    /// </summary>
    private async Task<ListingServiceResult<Listing>> GetEditableAvailabilityListingAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken)
    {
        var owned = await GetOwnedListingAsync(listingId, user, cancellationToken);
        return owned.IsSuccess && owned.Value!.Status == ListingStatus.Archived ? ArchivedFailure() : owned;
    }

    /// <summary>
    /// Validates the entire proposed date configuration against stored approved and active reservations.
    /// </summary>
    private async Task<ListingServiceResult<bool>> ValidateAvailabilityStateAsync(int listingId, ListingStatus status,
        IReadOnlyList<ListingAvailabilityPeriod> windows, IReadOnlyList<AvailabilityBlock> blocks, CancellationToken cancellationToken)
    {
        var errors = ValidateAvailabilityWindows(windows).ToList();
        var seenBlocks = new HashSet<ListingAvailabilityPeriod>();
        foreach (var block in blocks)
        {
            if (block.StartDate == default || block.EndDate == default || block.StartDate >= block.EndDate)
            {
                errors.Add(new(nameof(Listing.AvailabilityBlocks), "Choose unavailable dates with the start before the end."));
            }
            else if (!seenBlocks.Add(new ListingAvailabilityPeriod(block.StartDate, block.EndDate)))
            {
                errors.Add(new(nameof(Listing.AvailabilityBlocks), "An availability block with these dates already exists."));
            }
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(block, new ValidationContext(block), results, validateAllProperties: true);
            errors.AddRange(results.SelectMany(result => result.MemberNames.DefaultIfEmpty(nameof(Listing.AvailabilityBlocks))
                .Select(field => new ListingValidationError(field, result.ErrorMessage ?? "The block value is invalid."))));
        }
        if (errors.Count > 0)
        {
            return ListingServiceResult<bool>.Failure(ListingServiceStatus.ValidationFailed, errors.ToArray());
        }

        var reservations = await context.RentalRequests.AsNoTracking()
            .Where(request => request.ListingId == listingId && (request.Status == RentalStatus.Approved || request.Status == RentalStatus.Active))
            .Select(request => new ListingAvailabilityPeriod(request.StartDate, request.EndDate)).ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            if (!CoversPeriod(windows, reservation)
                || blocks.Any(block => block.StartDate < reservation.EndDate && reservation.StartDate < block.EndDate))
            {
                return ListingServiceResult<bool>.Failure(ListingServiceStatus.Conflict,
                    new ListingValidationError(nameof(Listing.RentalRequests), "This change would invalidate an approved or active rental reservation."));
            }
        }
        if (status != ListingStatus.Draft && windows.Count == 0)
        {
            return ListingServiceResult<bool>.Failure(ListingServiceStatus.ValidationFailed,
                new ListingValidationError(nameof(Listing.AvailabilityWindows), "A published or unavailable listing must retain at least one availability window."));
        }
        return ListingServiceResult<bool>.Success(true);
    }

    /// <summary>
    /// Checks complete half-open coverage by the union of adjacent or overlapping windows.
    /// </summary>
    private static bool CoversPeriod(IReadOnlyList<ListingAvailabilityPeriod> windows, ListingAvailabilityPeriod requested)
    {
        var coveredThrough = requested.StartDate;
        foreach (var window in windows.OrderBy(window => window.StartDate).ThenBy(window => window.EndDate))
        {
            if (window.EndDate <= coveredThrough)
            {
                continue;
            }
            if (window.StartDate > coveredThrough)
            {
                return false;
            }
            coveredThrough = window.EndDate;
            if (coveredThrough >= requested.EndDate)
            {
                return true;
            }
        }
        return false;
    }
}
