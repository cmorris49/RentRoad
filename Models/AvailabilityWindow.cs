namespace RentRoad.Models;

/// <summary>
/// Defines a period when a listing may be rented, including StartDate and excluding EndDate.
/// </summary>
/// <remarks>
/// Services may combine adjacent windows and must exclude blocks and approved or active rentals.
/// A listing without windows is unavailable for rental.
/// </remarks>
public class AvailabilityWindow
{
    public int AvailabilityWindowId { get; set; }

    public int ListingId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public Listing Listing { get; set; } = null!;
}
