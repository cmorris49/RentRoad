using RentRoad.Models.Enums;

namespace RentRoad.Services.Rentals;

/// <summary>Bounds one-based history requests without accepting a participant identifier.</summary>
public sealed record RentalHistoryParameters(int PageNumber = 1, int PageSize = 20)
{
    public const int DEFAULT_PAGE_SIZE = 20;
    public const int MAX_PAGE_SIZE = 100;
}

/// <summary>Identifies an image for existing authorized delivery without exposing its storage address.</summary>
public sealed record RentalHistoryImage(int ListingImageId, string? AltText);

/// <summary>Provides display hints only; every transition must still pass authoritative transactional service validation.</summary>
public sealed record RentalAvailableActions(bool CanCancel, bool CanApprove, bool CanDecline, bool CanActivate, bool CanComplete);

/// <summary>Contains safe history terms and counterparty display information, without Identity IDs or navigation graphs.</summary>
public sealed record RentalHistoryItem(int RentalRequestId, int ListingId, string? ListingTitle,
    RentalHistoryImage? PrimaryImage, string CounterpartyDisplayName, long DailyPriceAtRequestCents, long TotalPriceCents,
    DateOnly StartDate, DateOnly EndDate, TimeOnly PickupTime, TimeOnly ReturnTime, RentalStatus Status,
    DateTime CreatedAtUtc, DateTime? DecisionAtUtc, DateTime? CompletedAtUtc, RentalAvailableActions Actions)
{
    /// <summary>Identifies current public visibility for safe historical navigation; unknown visibility stays private.</summary>
    public bool IsListingPublic { get; init; }

    /// <summary>References only the conversation matching this already authorized listing/renter pair.</summary>
    public int? ConversationId { get; init; }
}

/// <summary>Returns a deterministic page, preserving an out-of-range page as an empty result.</summary>
public sealed record RentalHistoryPage(IReadOnlyList<RentalHistoryItem> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => (int)(((long)TotalCount + PageSize - 1) / PageSize);
}

/// <summary>Separates authentication, invalid pagination and dependency failures from a valid empty history.</summary>
public sealed record RentalHistoryResult(RentalRequestServiceStatus Status, RentalHistoryPage? Value, IReadOnlyList<RentalRequestError> Errors)
{
    public bool IsSuccess => Status == RentalRequestServiceStatus.Success;
}
