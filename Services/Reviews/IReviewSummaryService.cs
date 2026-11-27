namespace RentRoad.Services.Reviews;

/// <summary>
/// Summarizes customer feedback for a listing using the stored, completed reviews.
/// </summary>
public interface IReviewSummaryService
{
    /// <summary>
    /// Reads the current persistent AI summary without contacting the provider.
    /// </summary>
    /// <param name="listingId">The listing identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The aggregated review summary for the listing.</returns>
    Task<ReviewSummary> GetSummaryAsync(
        int listingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a summary only for an authenticated owner of a published listing.
    /// </summary>
    /// <param name="listingId">The published listing identifier.</param>
    /// <param name="user">The authenticated requesting principal.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The cache result or a safe generation outcome.</returns>
    Task<ReviewSummary> GenerateAsync(
        int listingId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}
