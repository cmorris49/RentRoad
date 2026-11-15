using System.Data.Common;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace RentRoad.Services.Listings;

/// <summary>
/// Permanently deletes only owned, photo-free listings without protected history.
/// </summary>
public sealed partial class ListingService
{
    /// <summary>
    /// Deletes an unreferenced listing and its scheduling metadata atomically without invoking Blob Storage.
    /// </summary>
    /// <param name="listingId">The existing listing identifier.</param>
    /// <param name="user">The authenticated owner principal.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, an authorization/not-found result, or a conflict explaining protected dependencies.</returns>
    public async Task<ListingServiceResult<bool>> DeleteAsync(
        int listingId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ListingOperationScope.ExecuteAsync(context, listingId, user, async (database, token) =>
            {
                var owned = await ListingOperationScope.GetOwnedListingAsync(database, listingId, user, token);

                if (!owned.IsSuccess)
                {
                    return ListingServiceResult<bool>.Failure(owned.Status, owned.Errors.ToArray());
                }

                await ListingTransactionLock.AcquireAsync(database, listingId, token);
                var history = await database.RentalRequests.AnyAsync(row => row.ListingId == listingId, token)
                    || await database.Conversations.AnyAsync(row => row.ListingId == listingId, token)
                    || await database.UserActivities.AnyAsync(row => row.ListingId == listingId, token);

                if (history || await database.ListingImages.AnyAsync(row => row.ListingId == listingId, token))
                {
                    return ListingServiceResult<bool>.Failure(ListingServiceStatus.Conflict,
                        new ListingValidationError(string.Empty, SharedResource.Get(history
                            ? "ListingDeletion.HistoryProtected" : "ListingDeletion.RemovePhotos")));
                }

                database.AvailabilityWindows.RemoveRange(await database.AvailabilityWindows
                    .Where(row => row.ListingId == listingId).ToListAsync(token));
                database.AvailabilityBlocks.RemoveRange(await database.AvailabilityBlocks
                    .Where(row => row.ListingId == listingId).ToListAsync(token));
                database.Listings.Remove(owned.Value!);
                await database.SaveChangesAsync(token);
                return ListingServiceResult<bool>.Success(true);
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return ListingServiceResult<bool>.Failure(ListingServiceStatus.Conflict,
                new ListingValidationError(string.Empty, SharedResource.Get("ListingDeletion.Unavailable")));
        }
    }
}
