using System.ComponentModel.DataAnnotations;
using RentRoad.Models.Enums;

namespace RentRoad.Models;

/// <summary>
/// Records a rental request and the terms agreed when it was submitted.
/// </summary>
/// <remarks>
/// Services must keep the listing, renter, dates, and agreed prices immutable after submission.
/// The rental period includes StartDate and excludes EndDate. Billable days are the date difference;
/// pickup and return times are informational and do not affect pricing.
/// </remarks>
public class RentalRequest
{
    public int RentalRequestId { get; set; }

    public int ListingId { get; set; }

    [Required]
    public string RenterId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the inclusive start date.
    /// </summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Gets or sets the exclusive end date.
    /// </summary>
    public DateOnly EndDate { get; set; }

    public TimeOnly PickupTime { get; set; }

    public TimeOnly ReturnTime { get; set; }

    /// <summary>
    /// Gets or sets the daily price in CAD cents agreed at submission.
    /// </summary>
    public long DailyPriceAtRequestCents { get; set; }

    /// <summary>
    /// Gets or sets the total price in CAD cents agreed at submission.
    /// </summary>
    public long TotalPriceCents { get; set; }

    public RentalStatus Status { get; set; } = RentalStatus.Pending;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? DecisionAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public Listing Listing { get; set; } = null!;

    public ApplicationUser Renter { get; set; } = null!;

    public Review? Review { get; set; }
}
