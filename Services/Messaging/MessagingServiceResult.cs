namespace RentRoad.Services.Messaging;

/// <summary>Identifies messaging outcomes without coupling persistence to HTTP, Razor or SignalR.</summary>
public enum MessagingServiceStatus
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

/// <summary>Describes a field error with a shared resource key and formatting arguments for the presentation boundary.</summary>
public sealed record MessagingError(string Field, string ResourceKey, params object[] Arguments);

/// <summary>Returns safe detached data, structured failures and explicit conversation/message replay outcomes.</summary>
public sealed record MessagingResult<T>(MessagingServiceStatus Status, T? Value, IReadOnlyList<MessagingError> Errors)
{
    public bool IsSuccess => Status == MessagingServiceStatus.Success;
    public bool AlreadyExists { get; init; }
    public bool AlreadyApplied { get; init; }

    /// <summary>Returns confirmed data, distinguishing an existing conversation or an already-persisted message/read acknowledgment.</summary>
    internal static MessagingResult<T> Success(T value, bool alreadyExists = false, bool alreadyApplied = false) =>
        new(MessagingServiceStatus.Success, value, Array.Empty<MessagingError>()) { AlreadyExists = alreadyExists, AlreadyApplied = alreadyApplied };

    /// <summary>Returns resource-backed errors without exposing database entities or inferred success.</summary>
    internal static MessagingResult<T> Failure(MessagingServiceStatus status, params MessagingError[] errors) => new(status, default, errors);
}
