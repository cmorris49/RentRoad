namespace RentRoad.Services.Reviews;

/// <summary>
/// Contains the aggregate summary for a listing's completed rental reviews.
/// </summary>
/// <param name="ListingId">The listing identifier associated with the review set.</param>
/// <param name="AverageRating">The average rating across all completed reviews.</param>
/// <param name="ReviewCount">The total number of reviews included in the summary.</param>
/// <param name="SummaryText">The generated summary statement.</param>
/// <param name="GeneratedAtUtc">The UTC timestamp when the summary was generated.</param>
public sealed record ReviewSummary(
    int ListingId,
    double AverageRating,
    int ReviewCount,
    string SummaryText,
    DateTime GeneratedAtUtc);
