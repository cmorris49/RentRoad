namespace RentRoad.Services.Listings;

/// <summary>
/// Contains an owner's unavailable date range and optional private reason.
/// </summary>
/// <remarks>
/// StartDate is inclusive and EndDate is exclusive. Listing ownership and record IDs are supplied separately.
/// </remarks>
public sealed record ListingAvailabilityBlockUpdate(DateOnly StartDate, DateOnly EndDate, string? Reason = null);
