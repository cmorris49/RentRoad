using RentRoad.Models.Enums;

namespace RentRoad.Services.Listings;

/// <summary>
/// Contains a bounded public result page and the total number of matching listings.
/// </summary>
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
public sealed record ListingSummary(
    int ListingId,
    string? Title,
    ListingCategory? Category,
    ItemCondition? Condition,
    long DailyPriceCents,
    string? PickupArea,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    ListingImageInfo? CoverImage);

/// <summary>
/// Contains published listing details without account identifiers, Identity entities, or private owner data.
/// </summary>
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
    IReadOnlyList<ListingAvailabilityPeriod> AvailabilityBlocks);

/// <summary>
/// Describes a stored listing image and its presentation order without exposing its original filename.
/// </summary>
public sealed record ListingImageInfo(
    int ListingImageId,
    string ContainerName,
    string BlobName,
    string ContentType,
    string? AltText,
    int DisplayOrder,
    bool IsPrimary);
