using System.ComponentModel.DataAnnotations;

namespace RentRoad.Models;

public class Review
{
    public int ReviewId { get; set; }

    public int RentalRequestId { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(2000)]
    public string? Comment { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public RentalRequest RentalRequest { get; set; } = null!;

}
