namespace RentRoad.Services.Listings;

/// <summary>
/// Identifies the outcome of a listing management operation.
/// </summary>
public enum ListingServiceStatus
{
    Success,
    Unauthenticated,
    Forbidden,
    NotFound,
    ValidationFailed,
    InvalidState,
    Conflict,
    StorageFailed,
    PersistenceFailed,
    CleanupPending,
    ReconciliationRequired
}

/// <summary>
/// Describes a listing validation error and its associated field.
/// </summary>
/// <param name="Field">The field associated with a validation error.</param>
/// <param name="Message">The message text associated with the operation.</param>
public sealed record ListingValidationError(string Field, string Message);

/// <summary>
/// Carries a listing operation's value or actionable errors without depending on MVC.
/// </summary>
/// <typeparam name="T">The operation's result value type.</typeparam>
/// <param name="Status">The operation or lifecycle status associated with the value.</param>
/// <param name="Value">The operation's result value, when available.</param>
/// <param name="Errors">The validation or operation errors associated with the result.</param>
public sealed record ListingServiceResult<T>(
    ListingServiceStatus Status,
    T? Value,
    IReadOnlyList<ListingValidationError> Errors)
{
    public bool IsSuccess => Status == ListingServiceStatus.Success;

    /// <summary>
    /// Creates a successful result containing the operation's value.
    /// </summary>
    /// <param name="value">The operation's result value, when available.</param>
    /// <returns>The operation result containing the returned value or structured errors.</returns>
    public static ListingServiceResult<T> Success(T value) =>
        new(ListingServiceStatus.Success, value, Array.Empty<ListingValidationError>());

    /// <summary>
    /// Creates an unsuccessful result with field-specific errors.
    /// </summary>
    /// <param name="status">The operation or lifecycle status associated with the value.</param>
    /// <param name="errors">The validation or operation errors associated with the result.</param>
    /// <returns>The operation result containing the returned value or structured errors.</returns>
    public static ListingServiceResult<T> Failure(
        ListingServiceStatus status,
        params ListingValidationError[] errors) =>
        new(status, default, errors);
}
