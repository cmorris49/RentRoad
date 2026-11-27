namespace RentRoad.Services.Reviews;

/// <summary>
/// Carries only a public review's associated rating and minimized comment to the provider.
/// </summary>
/// <param name="Rating">The public star rating.</param>
/// <param name="Comment">The bounded, redacted public comment, if any.</param>
public sealed record ReviewSummaryInput(int Rating, string? Comment);

/// <summary>
/// Describes summary availability without exposing provider or configuration details.
/// </summary>
public enum ReviewSummaryState
{
    Deterministic,
    Current,
    NotGenerated,
    InsufficientReviews,
    NotConfigured,
    Busy,
    RateLimited,
    Unavailable,
    InvalidResponse,
    ReviewsChanged,
    NotFound,
    Forbidden
}

/// <summary>
/// Contains a validated provider response or an operational failure category.
/// </summary>
/// <param name="State">The safe outcome category.</param>
/// <param name="Text">Validated summary text on success.</param>
public sealed record GeminiSummaryResult(ReviewSummaryState State, string? Text = null);

/// <summary>
/// Provides a mockable boundary for the single, bounded external model request.
/// </summary>
public interface IGeminiReviewSummarizer
{
    /// <summary>
    /// Summarizes untrusted public review data without automatic retries.
    /// </summary>
    /// <param name="reviews">The minimized public rating/comment pairs.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The validated text or a safe failure category.</returns>
    Task<GeminiSummaryResult> GenerateAsync(
        IReadOnlyList<ReviewSummaryInput> reviews,
        CancellationToken cancellationToken = default);
}
