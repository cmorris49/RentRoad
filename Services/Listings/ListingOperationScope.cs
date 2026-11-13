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
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <returns>The authenticated account identifier, or null when none is available.</returns>
    internal static string? GetOwnerId(ClaimsPrincipal? user)
    {
        var id = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>
    /// Loads a listing and verifies its owner without accepting a submitted owner identifier.
    /// </summary>
    /// <param name="context">The existing scoped database context.</param>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    internal static async Task<ListingServiceResult<Listing>> GetOwnedListingAsync(
        ApplicationDbContext context,
        int listingId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken)
    {
        var ownerId = GetOwnerId(user);

        if (ownerId is null)
        {
            return ListingServiceResult<Listing>.Failure(
                ListingServiceStatus.Unauthenticated,
                new ListingValidationError(
                    nameof(Listing.OwnerId),
                    SharedResource.Get("ListingValidation.SignInToManageYourListings")));
        }

        var listing = await context.Listings.SingleOrDefaultAsync(
            item => item.ListingId == listingId,
            cancellationToken);

        if (listing is null)
        {
            return ListingServiceResult<Listing>.Failure(
                ListingServiceStatus.NotFound,
                new ListingValidationError(
                    nameof(Listing.ListingId),
                    SharedResource.Get("ListingValidation.TheListingCouldNotBeFound")));
        }

        return string.Equals(listing.OwnerId, ownerId, StringComparison.Ordinal)
            ? ListingServiceResult<Listing>.Success(listing)
            : ListingServiceResult<Listing>.Failure(
                ListingServiceStatus.Forbidden,
                new ListingValidationError(
                    nameof(Listing.OwnerId),
                    SharedResource.Get("ListingValidation.OnlyTheListingOwnerCanPerformThisAction")));
    }

    /// <summary>
    /// Constructs a separate tracker using the scoped context's exact provider options and runtime model.
    /// </summary>
    /// <param name="context">The existing scoped database context.</param>
    /// <returns>The independent database context using the existing provider and model.</returns>
    internal static ApplicationDbContext CreateContext(ApplicationDbContext context) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>((DbContextOptions<ApplicationDbContext>)context.GetService<IDbContextOptions>())
                .UseModel(context.Model).Options);

    /// <summary>
    /// Rejects pending listing/availability changes, optional image changes, and caller transactions before an
    /// independent write.
    /// </summary>
    /// <param name="context">The existing scoped database context.</param>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="includeImageChanges">Whether pending image changes are included in caller-state checks.</param>
    /// <returns>The operation result containing the returned value or structured errors.</returns>
    internal static ListingServiceResult<bool> CheckCallerState(
        ApplicationDbContext context,
        int listingId,
        ClaimsPrincipal? user,
        bool includeImageChanges = true)
    {
        if (GetOwnerId(user) is null)
        {
            return ListingServiceResult<bool>.Failure(
                ListingServiceStatus.Unauthenticated,
                new ListingValidationError(
                    nameof(Listing.OwnerId),
                    SharedResource.Get("ListingValidation.SignInToManageYourListings")));
        }

        return context.Database.CurrentTransaction is not null
            || TrackedEntries(context, listingId)
                .Any(entry => entry.State != EntityState.Unchanged && (includeImageChanges || entry.Entity is not ListingImage))
            ? ListingServiceResult<bool>.Failure(
                ListingServiceStatus.Conflict,
                new ListingValidationError(
                    nameof(Listing.ListingId),
                    SharedResource.Get("ListingValidation.SaveOrDiscardPendingListingChangesAndCompleteThe")))
            : ListingServiceResult<bool>.Success(true);
    }

    /// <summary>
    /// Rechecks and commits a whole listing mutation inside a serializable transaction on every provider retry.
    /// </summary>
    /// <typeparam name="T">The operation's result value type.</typeparam>
    /// <param name="context">The existing scoped database context.</param>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="operation">The asynchronous operation to execute within the surrounding service workflow.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <param name="includeImageChanges">Whether pending image changes are included in caller-state checks.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    internal static async Task<ListingServiceResult<T>> ExecuteAsync<T>(
        ApplicationDbContext context,
        int listingId,
        ClaimsPrincipal? user,
        Func<ApplicationDbContext, CancellationToken, Task<ListingServiceResult<T>>> operation,
        CancellationToken cancellationToken,
        bool includeImageChanges = true)
    {
        var ready = CheckCallerState(context, listingId, user, includeImageChanges);

        if (!ready.IsSuccess)
        {
            return ListingServiceResult<T>.Failure(ready.Status, ready.Errors.ToArray());
        }

        var result = await context.Database.CreateExecutionStrategy().ExecuteAsync(
            async token =>
            {
                await using var writeContext = CreateContext(context);
                await using var transaction = await writeContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    token);
                var outcome = await operation(writeContext, token);

                if (outcome.IsSuccess)
                {
                    await transaction.CommitAsync(token);
                }

                return outcome;
            },
            cancellationToken);

        if (result.IsSuccess)
        {
            InvalidateTrackedEntries(context, listingId);
        }

        return result;
    }

    /// <summary>
    /// Discards unchanged cached entities after independently committed or verified listing changes.
    /// </summary>
    /// <param name="context">The existing scoped database context.</param>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    internal static void InvalidateTrackedEntries(ApplicationDbContext context, int listingId)
    {
        foreach (var entry in TrackedEntries(context, listingId)
            .Where(entry => entry.State == EntityState.Unchanged)
            .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Selects only this listing's tracked parent, availability, and image entities.
    /// </summary>
    /// <param name="context">The existing scoped database context.</param>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <returns>The tracked entries associated with the selected listing.</returns>
    private static IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> TrackedEntries(
        ApplicationDbContext context,
        int listingId) =>
        context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is Listing listing && listing.ListingId == listingId
                || entry.Entity is AvailabilityWindow window && window.ListingId == listingId
                || entry.Entity is AvailabilityBlock block && block.ListingId == listingId
                || entry.Entity is ListingImage image && image.ListingId == listingId);
}
