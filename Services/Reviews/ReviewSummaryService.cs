namespace RentRoad.Services.Reviews;

/// <summary>
/// Builds a filtered review summary using stored completed rental reviews.
/// </summary>
public sealed class ReviewSummaryService : IReviewSummaryService
{
    private readonly IReviewService reviewService;

    /// <summary>
    /// Initializes the summary service with the underlying review component.
    /// </summary>
    /// <param name="reviewService">The review service that exposes stored reviews and aggregates.</param>
    public ReviewSummaryService(IReviewService reviewService)
    {
        this.reviewService = reviewService;
    }

    /// <inheritdoc />
    public Task<ReviewSummary> GetSummaryAsync(
        int listingId,
        CancellationToken cancellationToken = default) =>
        reviewService.GetSummaryAsync(listingId, cancellationToken);
}
