namespace RentRoad.Services.Messaging;

/// <summary>
/// Identifies messaging outcomes without coupling persistence to HTTP, Razor or SignalR.
/// </summary>
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

/// <summary>
/// Describes a field error with a shared resource key and formatting arguments for the presentation boundary.
/// </summary>
/// <param name="Field">The field associated with a validation error.</param>
/// <param name="ResourceKey">The shared resource key describing the error.</param>
/// <param name="Arguments">The formatting arguments associated with the resource key.</param>
public sealed record MessagingError(string Field, string ResourceKey, params object[] Arguments);

/// <summary>
/// Returns safe detached data, structured failures and explicit conversation/message replay outcomes.
/// </summary>
/// <typeparam name="T">The operation's result value type.</typeparam>
/// <param name="Status">The operation or lifecycle status associated with the value.</param>
/// <param name="Value">The operation's result value, when available.</param>
/// <param name="Errors">The validation or operation errors associated with the result.</param>
public sealed record MessagingResult<T>(
    MessagingServiceStatus Status,
    T? Value,
    IReadOnlyList<MessagingError> Errors)
{
    public bool IsSuccess => Status == MessagingServiceStatus.Success;

    public bool AlreadyExists
    {
        get;
        init;
    }

    public bool AlreadyApplied
    {
        get;
        init;
    }

    /// <summary>
    /// Returns confirmed data, distinguishing an existing conversation or an already-persisted message/read
    /// acknowledgment.
    /// </summary>
    /// <param name="value">The operation's result value, when available.</param>
    /// <param name="alreadyExists">Whether the requested conversation already existed.</param>
    /// <param name="alreadyApplied">Whether the requested operation was already persisted.</param>
    /// <returns>The operation result containing the returned value or structured errors.</returns>
    internal static MessagingResult<T> Success(
        T value,
        bool alreadyExists = false,
        bool alreadyApplied = false) =>
        new(MessagingServiceStatus.Success, value, Array.Empty<MessagingError>())
        {
            AlreadyExists = alreadyExists,
            AlreadyApplied = alreadyApplied
        };

    /// <summary>
    /// Returns resource-backed errors without exposing database entities or inferred success.
    /// </summary>
    /// <param name="status">The operation or lifecycle status associated with the value.</param>
    /// <param name="errors">The validation or operation errors associated with the result.</param>
    /// <returns>The operation result containing the returned value or structured errors.</returns>
    internal static MessagingResult<T> Failure(MessagingServiceStatus status, params MessagingError[] errors) => new(
            status,
            default,
            errors);
}
