namespace RentRoad.Services.Rentals;

/// <summary>
/// Accepts only a listing and half-open calendar period; optional informational times default to midnight and do
/// not affect pricing.
/// </summary>
/// <param name="ListingId">The identifier of the listing being processed.</param>
/// <param name="StartDate">The inclusive start date of the rental or availability period.</param>
/// <param name="EndDate">The exclusive end date of the rental or availability period.</param>
/// <param name="PickupTime">The informational pickup time.</param>
/// <param name="ReturnTime">The informational return time.</param>
public sealed record RentalRequestCreate(
    int ListingId,
    DateOnly StartDate,
    DateOnly EndDate,
    TimeOnly PickupTime = default,
    TimeOnly ReturnTime = default);
