using RentRoad.Models.Enums;
using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Contains published item information, owner display name, image metadata, and public availability dates.
/// </summary>
public sealed record ListingDetailsViewModel(int ListingId, string? Title, string? Description, ListingCategory? Category,
    ItemCondition? Condition, ListingPriceViewModel DailyPrice, string? PickupArea, double? Latitude, double? Longitude,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? PublishedAtUtc, string OwnerDisplayName,
    IReadOnlyList<ListingImageViewModel> Images, IReadOnlyList<ListingAvailabilityViewModel> AvailabilityWindows,
    IReadOnlyList<ListingAvailabilityViewModel> AvailabilityBlocks)
{
    public bool IsOwnedByViewer { get; init; }

    /// <summary>Contains only an existing participant-authorized conversation matching this public listing.</summary>
    public int? ReturnConversationId { get; init; }

    public IReadOnlyList<ListingSummaryViewModel> SimilarListings { get; init; } = Array.Empty<ListingSummaryViewModel>();

    public IReadOnlyList<ListingReviewViewModel> Reviews { get; init; } = Array.Empty<ListingReviewViewModel>();

    public double? AverageRating => Reviews.Count == 0 ? null : Reviews.Average(review => review.Rating);

    /// <summary>
    /// Places the primary image first and retains configured display order, with a stable ID tie-breaker, for remaining photos.
    /// </summary>
    public IReadOnlyList<ListingImageViewModel> GalleryImages => Images.OrderByDescending(image => image.IsPrimary)
        .ThenBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId).ToArray();

    /// <summary>
    /// Maps the existing public-only service projection without introducing Identity fields or private availability notes.
    /// </summary>
    public static ListingDetailsViewModel FromResult(ListingDetails listing) => new(listing.ListingId, listing.Title,
        listing.Description, listing.Category, listing.Condition, new ListingPriceViewModel(listing.DailyPriceCents),
        listing.PickupArea, listing.Latitude, listing.Longitude, listing.CreatedAtUtc, listing.UpdatedAtUtc,
        listing.PublishedAtUtc, listing.OwnerDisplayName, listing.Images.Select(ListingImageViewModel.FromResult).ToArray(),
        listing.AvailabilityWindows.Select(ListingAvailabilityViewModel.FromResult).ToArray(),
        listing.AvailabilityBlocks.Select(ListingAvailabilityViewModel.FromResult).ToArray())
    {
        Reviews = listing.Reviews.Select(ListingReviewViewModel.FromResult).ToArray()
    };
}

/// <summary>
/// Contains the public presentation values for one listing review.
/// </summary>
public sealed record ListingReviewViewModel(
    string ReviewerDisplayName,
    int Rating,
    string? Comment,
    DateTime CreatedAtUtc)
{
    /// <summary>
    /// Maps a public service review projection to its presentation model.
    /// </summary>
    /// <param name="review">The public review projection.</param>
    /// <returns>The mapped review presentation model.</returns>
    public static ListingReviewViewModel FromResult(ListingReview review) => new(
        review.ReviewerDisplayName,
        review.Rating,
        review.Comment,
        review.CreatedAtUtc);
}
