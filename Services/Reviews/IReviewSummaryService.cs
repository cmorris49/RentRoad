namespace RentRoad.Services.Reviews;

/// <summary>
/// Summarizes customer feedback for a listing using the stored, completed reviews.
/// </summary>
public interface IReviewSummaryService
{
    /// <summary>
    /// Generates a summary for the specified listing using the visible rating and comment history.
    /// </summary>
    /// <param name="listingId">The listing identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The aggregated review summary for the listing.</returns>
    Task<ReviewSummary> GetSummaryAsync(
        int listingId,
        CancellationToken cancellationToken = default);
}
