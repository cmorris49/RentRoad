using RentRoad.Models;

namespace RentRoad.Services.Reviews;

/// <summary>
/// Identifies review operation outcomes without coupling the service to HTTP-specific behaviour.
/// </summary>
public enum ReviewServiceStatus
{
    Success,
    Unauthenticated,
    Forbidden,
    NotFound,
    ValidationFailed,
    Conflict,
    PersistenceFailed,
    ConcurrencyFailed
}

/// <summary>
/// Preserves the field and shared resource key associated with a review validation issue.
/// </summary>
/// <param name="Field">The field related to the review validation error.</param>
/// <param name="ResourceKey">The localized resource key for the error.</param>
public sealed record ReviewError(string Field, string ResourceKey)
{
    /// <summary>
    /// Resolves the error message from the shared application resource dictionary.
    /// </summary>
    public string Message => SharedResource.Get(ResourceKey);
}

/// <summary>
/// Returns the created review or any structured validation or authorization problems.
/// </summary>
/// <param name="Status">The outcome of the review operation.</param>
/// <param name="Value">The review created by the operation, when available.</param>
/// <param name="Errors">The validation or processing errors attached to the operation.</param>
public sealed record ReviewServiceResult(
    ReviewServiceStatus Status,
    Review? Value,
    IReadOnlyList<ReviewError> Errors)
{
    /// <summary>
    /// Indicates whether the operation succeeded and returned a persisted review.
    /// </summary>
    public bool IsSuccess => Status == ReviewServiceStatus.Success && Value is not null;

    /// <summary>
    /// Creates a successful review result with the stored review entity.
    /// </summary>
    /// <param name="review">The created review.</param>
    /// <returns>The successful operation result.</returns>
    internal static ReviewServiceResult Success(Review review) =>
        new(ReviewServiceStatus.Success, review, Array.Empty<ReviewError>());

    /// <summary>
    /// Creates a structured failed review result.
    /// </summary>
    /// <param name="status">The outcome status.</param>
    /// <param name="errors">The review validation or processing errors.</param>
    /// <returns>The failed operation result.</returns>
    internal static ReviewServiceResult Failure(
        ReviewServiceStatus status,
        params ReviewError[] errors) =>
        new(status, null, errors);
}
