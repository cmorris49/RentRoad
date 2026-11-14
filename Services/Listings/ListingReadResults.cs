using RentRoad.Models.Enums;

namespace RentRoad.Services.Listings;

/// <summary>
/// Contains a bounded public result page and the total number of matching listings.
/// </summary>
/// <param name="Items">The records included in this result page.</param>
/// <param name="TotalCount">The total number of matching records before pagination.</param>
/// <param name="PageNumber">The requested one-based page number.</param>
/// <param name="PageSize">The number of records requested per page.</param>
public sealed record ListingSearchPage(
    IReadOnlyList<ListingSummary> Items,
    int TotalCount,
    int PageNumber,
    int PageSize)
{
    public int TotalPages => TotalCount == 0 ? 0 : 1 + (TotalCount - 1) / PageSize;
}

/// <summary>
/// Contains the public information needed for a listing in a browse or search result.
/// </summary>
/// <param name="ListingId">The identifier of the listing being processed.</param>
/// <param name="Title">The listing title, when supplied.</param>
/// <param name="Category">The listing category, when supplied.</param>
/// <param name="Condition">The item condition, when supplied.</param>
/// <param name="DailyPriceCents">The daily rental price in integer CAD cents.</param>
/// <param name="PickupArea">The listing's pickup area, when supplied.</param>
/// <param name="CreatedAtUtc">The creation timestamp in UTC.</param>
/// <param name="PublishedAtUtc">The first publication timestamp in UTC, when recorded.</param>
/// <param name="CoverImage">The listing's selected cover image, when available.</param>
public sealed record ListingSummary(
    int ListingId,
    string? Title,
    ListingCategory? Category,
    ItemCondition? Condition,
    long DailyPriceCents,
    string? PickupArea,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    ListingImageInfo? CoverImage)
{
    public IReadOnlyList<ListingThumbnailInfo> Images
    {
        get;
        init;
    } = Array.Empty<ListingThumbnailInfo>();
}

/// <summary>
/// Contains ordered public gallery references without storage locations or original filenames.
/// </summary>
/// <param name="ListingImageId">The identifier of the listing image.</param>
/// <param name="AltText">The optional alternative text for the listing image.</param>
/// <param name="DisplayOrder">The image's zero-based display position.</param>
/// <param name="IsPrimary">Whether this image is the listing's primary image.</param>
public sealed record ListingThumbnailInfo(int ListingImageId, string? AltText, int DisplayOrder, bool IsPrimary);

/// <summary>
/// Contains published listing details without account identifiers, Identity entities, or private owner data.
/// </summary>
/// <param name="ListingId">The identifier of the listing being processed.</param>
/// <param name="Title">The listing title, when supplied.</param>
/// <param name="Description">The item description, when supplied.</param>
/// <param name="Category">The listing category, when supplied.</param>
/// <param name="Condition">The item condition, when supplied.</param>
/// <param name="DailyPriceCents">The daily rental price in integer CAD cents.</param>
/// <param name="PickupArea">The listing's pickup area, when supplied.</param>
/// <param name="Latitude">The optional listing latitude.</param>
/// <param name="Longitude">The optional listing longitude.</param>
/// <param name="CreatedAtUtc">The creation timestamp in UTC.</param>
/// <param name="UpdatedAtUtc">The most recent update timestamp in UTC.</param>
/// <param name="PublishedAtUtc">The first publication timestamp in UTC, when recorded.</param>
/// <param name="OwnerDisplayName">The listing owner's public display name.</param>
/// <param name="Images">The image metadata collection in the order being processed.</param>
/// <param name="AvailabilityWindows">The listing's availability windows.</param>
/// <param name="AvailabilityBlocks">The listing's unavailable date ranges.</param>
/// <remarks>
/// Availability blocks expose dates only; internal block reasons are not public listing information.
/// Image references contain storage metadata, not credentials or generated SAS URLs.
/// </remarks>
public sealed record ListingDetails(
    int ListingId,
    string? Title,
    string? Description,
    ListingCategory? Category,
    ItemCondition? Condition,
    long DailyPriceCents,
    string? PickupArea,
    double? Latitude,
    double? Longitude,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? PublishedAtUtc,
    string OwnerDisplayName,
    IReadOnlyList<ListingImageInfo> Images,
    IReadOnlyList<ListingAvailabilityPeriod> AvailabilityWindows,
    IReadOnlyList<ListingAvailabilityPeriod> AvailabilityBlocks,
    IReadOnlyList<ListingReview> Reviews);

/// <summary>
/// Contains the public information shown for a completed rental review.
/// </summary>
/// <param name="ReviewId">The identifier used to provide stable ordering.</param>
/// <param name="ReviewerDisplayName">The reviewer's public display name.</param>
/// <param name="Rating">The rating from one to five.</param>
/// <param name="Comment">The optional review comment.</param>
/// <param name="CreatedAtUtc">The time the review was created.</param>
public sealed record ListingReview(
    int ReviewId,
    string ReviewerDisplayName,
    int Rating,
    string? Comment,
    DateTime CreatedAtUtc);

/// <summary>
/// Describes a stored listing image and its presentation order without exposing its original filename.
/// </summary>
/// <param name="ListingImageId">The identifier of the listing image.</param>
/// <param name="ContainerName">The name of the image storage container.</param>
/// <param name="BlobName">The blob name within the configured storage container.</param>
/// <param name="ContentType">The image MIME content type.</param>
/// <param name="AltText">The optional alternative text for the listing image.</param>
/// <param name="DisplayOrder">The image's zero-based display position.</param>
/// <param name="IsPrimary">Whether this image is the listing's primary image.</param>
public sealed record ListingImageInfo(
    int ListingImageId,
    string ContainerName,
    string BlobName,
    string ContentType,
    string? AltText,
    int DisplayOrder,
    bool IsPrimary);
