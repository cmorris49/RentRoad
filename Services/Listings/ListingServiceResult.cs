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
    InvalidState
}

/// <summary>
/// Describes a listing validation error and its associated field.
/// </summary>
public sealed record ListingValidationError(string Field, string Message);

/// <summary>
/// Carries a listing operation's value or actionable errors without depending on MVC.
/// </summary>
public sealed record ListingServiceResult<T>(
    ListingServiceStatus Status,
    T? Value,
    IReadOnlyList<ListingValidationError> Errors)
{
    public bool IsSuccess => Status == ListingServiceStatus.Success;

    /// <summary>
    /// Creates a successful result containing the operation's value.
    /// </summary>
    public static ListingServiceResult<T> Success(T value) =>
        new(ListingServiceStatus.Success, value, Array.Empty<ListingValidationError>());

    /// <summary>
    /// Creates an unsuccessful result with field-specific errors.
    /// </summary>
    public static ListingServiceResult<T> Failure(ListingServiceStatus status, params ListingValidationError[] errors) =>
        new(status, default, errors);
}
