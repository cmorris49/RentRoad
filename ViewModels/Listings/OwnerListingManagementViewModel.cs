using RentRoad.Models;
using RentRoad.Models.Enums;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Presents all owner listings in service order without exposing account data or storage identifiers.
/// </summary>
public sealed class OwnerListingManagementViewModel
{
    public IReadOnlyList<OwnerListingSummaryViewModel> Listings { get; }

    /// <summary>
    /// Maps owned entities to a narrow presentation contract, including incomplete drafts and archived listings.
    /// </summary>
    public OwnerListingManagementViewModel(IReadOnlyList<Listing> listings)
    {
        Listings = listings.Select(OwnerListingSummaryViewModel.FromListing).ToArray();
    }
}

/// <summary>
/// Describes an owner's listing and its trusted lifecycle status, retaining nullable draft prices.
/// </summary>
public sealed record OwnerListingSummaryViewModel(int ListingId, string? Title, string? PickupArea,
    ListingPriceViewModel? DailyPrice, ListingStatus Status, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? PublishedAtUtc)
{
    public ListingImageViewModel? CoverImage { get; init; }

    public bool CanEdit => true;
    public string? DeletionRestriction { get; init; }
    public bool CanDelete => DeletionRestriction is null;

    /// <summary>
    /// Maps only listing-management display fields while reusing exact CAD price presentation.
    /// </summary>
    public static OwnerListingSummaryViewModel FromListing(Listing listing) => new(listing.ListingId, listing.Title,
        listing.PickupArea, listing.DailyPriceCents.HasValue ? new ListingPriceViewModel(listing.DailyPriceCents.Value) : null,
        listing.Status, listing.CreatedAtUtc, listing.UpdatedAtUtc, listing.PublishedAtUtc)
        {
            DeletionRestriction = listing.RentalRequests.Count > 0 || listing.Conversations.Count > 0 || listing.Activities.Count > 0
                ? SharedResource.Get("ListingDeletion.HistoryProtected") : listing.Images.Count > 0
                    ? SharedResource.Get("ListingDeletion.RemovePhotos") : null,
            CoverImage = listing.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.DisplayOrder)
                .ThenBy(image => image.ListingImageId).Select(image => new ListingImageViewModel(image.ListingImageId,
                    image.ContentType, image.AltText, image.DisplayOrder, image.IsPrimary)).FirstOrDefault()
        };
}
