namespace RentRoad.Services.Listings;

/// <summary>
/// Contains an owner's unavailable date range and optional private reason.
/// </summary>
/// <param name="StartDate">The inclusive start date of the rental or availability period.</param>
/// <param name="EndDate">The exclusive end date of the rental or availability period.</param>
/// <param name="Reason">The optional private reason for the unavailable period.</param>
/// <remarks>
/// StartDate is inclusive and EndDate is exclusive. Listing ownership and record IDs are supplied separately.
/// </remarks>
public sealed record ListingAvailabilityBlockUpdate(DateOnly StartDate, DateOnly EndDate, string? Reason = null);
