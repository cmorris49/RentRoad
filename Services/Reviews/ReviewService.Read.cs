using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RentRoad.Models.Enums;

namespace RentRoad.Services.Reviews;

/// <summary>
/// Projects authenticated review eligibility and public-summary visibility in SQL.
/// </summary>
public sealed partial class ReviewService
{
    /// <inheritdoc />
    public Task<bool> IsPublishedAsync(int listingId, CancellationToken cancellationToken = default) =>
        context.Listings.AsNoTracking().AnyAsync(listing => listing.ListingId == listingId
            && listing.Status == ListingStatus.Published, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReviewRentalChoice>> GetEligibleRentalsAsync(
        int listingId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        var id = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(id) || listingId <= 0)
        {
            return Array.Empty<ReviewRentalChoice>();
        }

        return await context.RentalRequests.AsNoTracking()
            .Where(request => request.ListingId == listingId && request.Listing.Status == ListingStatus.Published
                && request.RenterId == id && request.Listing.OwnerId != id
                && request.Status == RentalStatus.Completed && request.Review == null)
            .OrderByDescending(request => request.EndDate).ThenByDescending(request => request.RentalRequestId)
            .Select(request => new ReviewRentalChoice(request.RentalRequestId, request.StartDate, request.EndDate))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReviewEditorData?> GetEditorAsync(
        int rentalRequestId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        var id = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return await context.RentalRequests.AsNoTracking()
            .Where(request => request.RentalRequestId == rentalRequestId && request.RenterId == id
                && request.Listing.OwnerId != id && request.Status == RentalStatus.Completed)
            .Select(request => new ReviewEditorData(request.RentalRequestId, request.ListingId,
                request.Review == null ? null : (int?)request.Review.Rating,
                request.Review == null ? null : request.Review.Comment))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
