using RentRoad.Models.Enums;

namespace RentRoad.Services.Rentals;

/// <summary>
/// Bounds one-based history requests without accepting a participant identifier.
/// </summary>
/// <param name="PageNumber">The requested one-based page number.</param>
/// <param name="PageSize">The number of records requested per page.</param>
public sealed record RentalHistoryParameters(int PageNumber = 1, int PageSize = 20)
{
    public const int DEFAULT_PAGE_SIZE = 20;
    public const int MAX_PAGE_SIZE = 100;
}

/// <summary>
/// Identifies an image for existing authorized delivery without exposing its storage address.
/// </summary>
/// <param name="ListingImageId">The identifier of the listing image.</param>
/// <param name="AltText">The optional alternative text for the listing image.</param>
public sealed record RentalHistoryImage(int ListingImageId, string? AltText);

/// <summary>
/// Provides display hints only; every transition must still pass authoritative transactional service validation.
/// </summary>
/// <param name="CanCancel">Whether cancellation is offered as a display hint.</param>
/// <param name="CanApprove">Whether approval is offered as a display hint.</param>
/// <param name="CanDecline">Whether decline is offered as a display hint.</param>
/// <param name="CanActivate">Whether activation is offered as a display hint.</param>
/// <param name="CanComplete">Whether completion is offered as a display hint.</param>
public sealed record RentalAvailableActions(
    bool CanCancel,
    bool CanApprove,
    bool CanDecline,
    bool CanActivate,
    bool CanComplete);

/// <summary>
/// Contains safe history terms and counterparty display information, without Identity IDs or navigation graphs.
/// </summary>
/// <param name="RentalRequestId">The identifier of the rental request.</param>
/// <param name="ListingId">The identifier of the listing being processed.</param>
/// <param name="ListingTitle">The listing title, when supplied.</param>
/// <param name="PrimaryImage">The primary listing image reference, when available.</param>
/// <param name="CounterpartyDisplayName">The other participant's public display name.</param>
/// <param name="DailyPriceAtRequestCents">The agreed daily price snapshot in integer CAD cents.</param>
/// <param name="TotalPriceCents">The agreed total rental price in integer CAD cents.</param>
/// <param name="StartDate">The inclusive start date of the rental or availability period.</param>
/// <param name="EndDate">The exclusive end date of the rental or availability period.</param>
/// <param name="PickupTime">The informational pickup time.</param>
/// <param name="ReturnTime">The informational return time.</param>
/// <param name="Status">The operation or lifecycle status associated with the value.</param>
/// <param name="CreatedAtUtc">The creation timestamp in UTC.</param>
/// <param name="DecisionAtUtc">The request decision timestamp in UTC, when recorded.</param>
/// <param name="CompletedAtUtc">The completion timestamp in UTC, when recorded.</param>
/// <param name="ReviewRating">The submitted review rating, when one exists.</param>
/// <param name="ReviewComment">The submitted review comment, when one exists.</param>
/// <param name="Actions">The advisory rental actions available to the viewer.</param>
public sealed record RentalHistoryItem(
    int RentalRequestId,
    int ListingId,
    string? ListingTitle,
    RentalHistoryImage? PrimaryImage,
    string CounterpartyDisplayName,
    long DailyPriceAtRequestCents,
    long TotalPriceCents,
    DateOnly StartDate,
    DateOnly EndDate,
    TimeOnly PickupTime,
    TimeOnly ReturnTime,
    RentalStatus Status,
    DateTime CreatedAtUtc,
    DateTime? DecisionAtUtc,
    DateTime? CompletedAtUtc,
    int? ReviewRating,
    string? ReviewComment,
    RentalAvailableActions Actions)
{
    /// <summary>
    /// Identifies current public visibility for safe historical navigation; unknown visibility stays private.
    /// </summary>
    public bool IsListingPublic
    {
        get;
        init;
    }

    /// <summary>
    /// References only the conversation matching this already authorized listing/renter pair.
    /// </summary>
    public int? ConversationId
    {
        get;
        init;
    }

    /// <summary>
    /// Identifies a completed renter rental that is eligible for one review submission.
    /// </summary>
    public bool CanReview
    {
        get;
        init;
    }
}

/// <summary>
/// Returns a deterministic page, preserving an out-of-range page as an empty result.
/// </summary>
/// <param name="Items">The records included in this result page.</param>
/// <param name="TotalCount">The total number of matching records before pagination.</param>
/// <param name="PageNumber">The requested one-based page number.</param>
/// <param name="PageSize">The number of records requested per page.</param>
public sealed record RentalHistoryPage(
    IReadOnlyList<RentalHistoryItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize)
{
    public int TotalPages => (int)(((long)TotalCount + PageSize - 1) / PageSize);
}

/// <summary>
/// Separates authentication, invalid pagination and dependency failures from a valid empty history.
/// </summary>
/// <param name="Status">The operation or lifecycle status associated with the value.</param>
/// <param name="Value">The operation's result value, when available.</param>
/// <param name="Errors">The validation or operation errors associated with the result.</param>
public sealed record RentalHistoryResult(
    RentalRequestServiceStatus Status,
    RentalHistoryPage? Value,
    IReadOnlyList<RentalRequestError> Errors)
{
    public bool IsSuccess => Status == RentalRequestServiceStatus.Success;
}
