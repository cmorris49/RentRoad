using RentRoad.Models;

namespace RentRoad.Services.Rentals;

/// <summary>
/// Identifies rental service outcomes without coupling services to HTTP responses.
/// </summary>
public enum RentalRequestServiceStatus
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
/// Preserves a stable resource key and field while resolving user-facing wording only when the message is read.
/// </summary>
/// <param name="Field">The field associated with a validation error.</param>
/// <param name="ResourceKey">The shared resource key describing the error.</param>
public sealed record RentalRequestError(string Field, string ResourceKey)
{
    public string Message => SharedResource.Get(ResourceKey);
}

/// <summary>
/// Returns a detached rental request, or errors, and distinguishes pending resubmissions and already-persisted
/// decision states.
/// </summary>
/// <param name="Status">The operation or lifecycle status associated with the value.</param>
/// <param name="Value">The operation's result value, when available.</param>
/// <param name="AlreadyPending">Whether an identical pending request was already stored.</param>
/// <param name="Errors">The validation or operation errors associated with the result.</param>
public sealed record RentalRequestServiceResult(
    RentalRequestServiceStatus Status,
    RentalRequest? Value,
    bool AlreadyPending,
    IReadOnlyList<RentalRequestError> Errors)
{
    /// <summary>
    /// Indicates that the requested target state was already persisted; this result does not represent another
    /// transition.
    /// </summary>
    public bool AlreadyApplied
    {
        get;
        init;
    }

    public bool IsSuccess => Status == RentalRequestServiceStatus.Success;

    /// <summary>
    /// Returns confirmed stored terms without exposing a tracked entity to the caller.
    /// </summary>
    /// <param name="request">The stored rental request whose terms and status are checked.</param>
    /// <param name="alreadyPending">Whether an identical pending request was already stored.</param>
    /// <param name="alreadyApplied">Whether the requested operation was already persisted.</param>
    /// <returns>The operation result containing the returned value or structured errors.</returns>
    internal static RentalRequestServiceResult Success(
        RentalRequest request,
        bool alreadyPending = false,
        bool alreadyApplied = false) =>
        new(RentalRequestServiceStatus.Success, request, alreadyPending, Array.Empty<RentalRequestError>())
        {
            AlreadyApplied = alreadyApplied
        };

    /// <summary>
    /// Returns structured failures without a request or any inferred successful insertion.
    /// </summary>
    /// <param name="status">The operation or lifecycle status associated with the value.</param>
    /// <param name="errors">The validation or operation errors associated with the result.</param>
    /// <returns>The operation result containing the returned value or structured errors.</returns>
    internal static RentalRequestServiceResult Failure(
        RentalRequestServiceStatus status,
        params RentalRequestError[] errors) =>
        new(status, null, false, errors);
}
