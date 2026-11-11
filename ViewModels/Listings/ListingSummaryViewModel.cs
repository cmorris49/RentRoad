using RentRoad.Models.Enums;
using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Contains only public browse-card information from the existing summary projection.
/// </summary>
public sealed record ListingSummaryViewModel(int ListingId, string? Title, ListingCategory? Category, ItemCondition? Condition,
    ListingPriceViewModel DailyPrice, string? PickupArea, DateTime CreatedAtUtc, DateTime? PublishedAtUtc, ListingImageViewModel? CoverImage)
{
    public IReadOnlyList<ListingGalleryPhotoViewModel> GalleryPhotos { get; init; } = Array.Empty<ListingGalleryPhotoViewModel>();

    /// <summary>
    /// Maps a public service result without loading entities or changing its deterministic ordering.
    /// </summary>
    public static ListingSummaryViewModel FromResult(ListingSummary listing) => new(listing.ListingId, listing.Title,
        listing.Category, listing.Condition, new ListingPriceViewModel(listing.DailyPriceCents), listing.PickupArea,
        listing.CreatedAtUtc, listing.PublishedAtUtc, listing.CoverImage is null ? null : ListingImageViewModel.FromResult(listing.CoverImage))
        {
            GalleryPhotos = listing.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.DisplayOrder)
                .ThenBy(image => image.ListingImageId)
                .Select(image => new ListingGalleryPhotoViewModel($"/listings/{listing.ListingId}/images/{image.ListingImageId}", image.AltText, image.IsPrimary)).ToArray()
        };
}
