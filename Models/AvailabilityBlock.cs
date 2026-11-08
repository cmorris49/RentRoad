using System.ComponentModel.DataAnnotations;

namespace RentRoad.Models;

/// <summary>
/// Defines an exception to listing availability, including StartDate and excluding EndDate.
/// </summary>
/// <remarks>
/// Approved and active rentals are separate sources of unavailability and must not be duplicated as blocks.
/// </remarks>
public class AvailabilityBlock
{
    public int AvailabilityBlockId { get; set; }

    public int ListingId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    [StringLength(250)]
    public string? Reason { get; set; }

    public Listing Listing { get; set; } = null!;
}
