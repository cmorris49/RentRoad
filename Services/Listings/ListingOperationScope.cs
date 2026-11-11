using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RentRoad.Data;
using RentRoad.Models;

namespace RentRoad.Services.Listings;

/// <summary>
/// Shares owner authorization and fresh, serializable units of work across listing services.
/// </summary>
internal static class ListingOperationScope
{
    /// <summary>
    /// Reads an owner identifier only from an authenticated Identity identity.
    /// </summary>
    internal static string? GetOwnerId(ClaimsPrincipal? user)
    {
        var id = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>
    /// Loads a listing and verifies its owner without accepting a submitted owner identifier.
    /// </summary>
    internal static async Task<ListingServiceResult<Listing>> GetOwnedListingAsync(ApplicationDbContext context, int listingId,
        ClaimsPrincipal? user, CancellationToken cancellationToken)
    {
        var ownerId = GetOwnerId(user);
        if (ownerId is null)
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.Unauthenticated,
                new ListingValidationError(nameof(Listing.OwnerId), SharedResource.Get("ListingValidation.SignInToManageYourListings")));
        }
        var listing = await context.Listings.SingleOrDefaultAsync(item => item.ListingId == listingId, cancellationToken);
        if (listing is null)
        {
            return ListingServiceResult<Listing>.Failure(ListingServiceStatus.NotFound,
                new ListingValidationError(nameof(Listing.ListingId), SharedResource.Get("ListingValidation.TheListingCouldNotBeFound")));
        }
        return string.Equals(listing.OwnerId, ownerId, StringComparison.Ordinal)
            ? ListingServiceResult<Listing>.Success(listing)
            : ListingServiceResult<Listing>.Failure(ListingServiceStatus.Forbidden,
                new ListingValidationError(nameof(Listing.OwnerId), SharedResource.Get("ListingValidation.OnlyTheListingOwnerCanPerformThisAction")));
    }

    /// <summary>
    /// Constructs a separate tracker using the scoped context's exact provider options and runtime model.
    /// </summary>
    internal static ApplicationDbContext CreateContext(ApplicationDbContext context) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>((DbContextOptions<ApplicationDbContext>)context.GetService<IDbContextOptions>())
            .UseModel(context.Model).Options);

    /// <summary>
    /// Rejects pending listing/availability changes, optional image changes, and caller transactions before an independent write.
    /// </summary>
    internal static ListingServiceResult<bool> CheckCallerState(ApplicationDbContext context, int listingId, ClaimsPrincipal? user,
        bool includeImageChanges = true)
    {
        if (GetOwnerId(user) is null)
        {
            return ListingServiceResult<bool>.Failure(ListingServiceStatus.Unauthenticated,
                new ListingValidationError(nameof(Listing.OwnerId), SharedResource.Get("ListingValidation.SignInToManageYourListings")));
        }
        return context.Database.CurrentTransaction is not null || TrackedEntries(context, listingId)
            .Any(entry => entry.State != EntityState.Unchanged && (includeImageChanges || entry.Entity is not ListingImage))
            ? ListingServiceResult<bool>.Failure(ListingServiceStatus.Conflict,
                new ListingValidationError(nameof(Listing.ListingId), SharedResource.Get("ListingValidation.SaveOrDiscardPendingListingChangesAndCompleteThe")))
            : ListingServiceResult<bool>.Success(true);
    }

    /// <summary>
    /// Rechecks and commits a whole listing mutation inside a serializable transaction on every provider retry.
    /// </summary>
    internal static async Task<ListingServiceResult<T>> ExecuteAsync<T>(ApplicationDbContext context, int listingId, ClaimsPrincipal? user,
        Func<ApplicationDbContext, CancellationToken, Task<ListingServiceResult<T>>> operation, CancellationToken cancellationToken,
        bool includeImageChanges = true)
    {
        var ready = CheckCallerState(context, listingId, user, includeImageChanges);
        if (!ready.IsSuccess)
        {
            return ListingServiceResult<T>.Failure(ready.Status, ready.Errors.ToArray());
        }
        var result = await context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var writeContext = CreateContext(context);
            await using var transaction = await writeContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var outcome = await operation(writeContext, token);
            if (outcome.IsSuccess)
            {
                await transaction.CommitAsync(token);
            }
            return outcome;
        }, cancellationToken);
        if (result.IsSuccess)
        {
            InvalidateTrackedEntries(context, listingId);
        }
        return result;
    }

    /// <summary>
    /// Discards unchanged cached entities after independently committed or verified listing changes.
    /// </summary>
    internal static void InvalidateTrackedEntries(ApplicationDbContext context, int listingId)
    {
        foreach (var entry in TrackedEntries(context, listingId).Where(entry => entry.State == EntityState.Unchanged).ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Selects only this listing's tracked parent, availability, and image entities.
    /// </summary>
    private static IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> TrackedEntries(ApplicationDbContext context, int listingId) =>
        context.ChangeTracker.Entries().Where(entry => entry.Entity is Listing listing && listing.ListingId == listingId
            || entry.Entity is AvailabilityWindow window && window.ListingId == listingId
            || entry.Entity is AvailabilityBlock block && block.ListingId == listingId
            || entry.Entity is ListingImage image && image.ListingId == listingId);
}
