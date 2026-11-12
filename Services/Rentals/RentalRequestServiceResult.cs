using RentRoad.Models;

namespace RentRoad.Services.Rentals;

/// <summary>Identifies rental service outcomes without coupling services to HTTP responses.</summary>
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

/// <summary>Preserves a stable resource key and field while resolving user-facing wording only when the message is read.</summary>
public sealed record RentalRequestError(string Field, string ResourceKey)
{
    public string Message => SharedResource.Get(ResourceKey);
}

/// <summary>Returns a detached rental request, or errors, and distinguishes pending resubmissions and already-persisted decision states.</summary>
public sealed record RentalRequestServiceResult(RentalRequestServiceStatus Status, RentalRequest? Value,
    bool AlreadyPending, IReadOnlyList<RentalRequestError> Errors)
{
    /// <summary>Indicates that the requested target state was already persisted; this result does not represent another transition.</summary>
    public bool AlreadyApplied { get; init; }

    public bool IsSuccess => Status == RentalRequestServiceStatus.Success;

    /// <summary>Returns confirmed stored terms without exposing a tracked entity to the caller.</summary>
    internal static RentalRequestServiceResult Success(RentalRequest request, bool alreadyPending = false, bool alreadyApplied = false) =>
        new(RentalRequestServiceStatus.Success, request, alreadyPending, Array.Empty<RentalRequestError>()) { AlreadyApplied = alreadyApplied };

    /// <summary>Returns structured failures without a request or any inferred successful insertion.</summary>
    internal static RentalRequestServiceResult Failure(RentalRequestServiceStatus status, params RentalRequestError[] errors) =>
        new(status, null, false, errors);
}
