namespace RentRoad.Services.Rentals;

/// <summary>Accepts only a listing and half-open calendar period; optional informational times default to midnight and do not affect pricing.</summary>
public sealed record RentalRequestCreate(int ListingId, DateOnly StartDate, DateOnly EndDate,
    TimeOnly PickupTime = default, TimeOnly ReturnTime = default);
