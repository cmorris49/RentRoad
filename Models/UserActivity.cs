using RentRoad.Models.Enums;

namespace RentRoad.Models;

public class UserActivity
{
    public int UserActivityId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int? ListingId { get; set; }

    /// <summary>
    /// Gets or sets the historical category observed during the activity, or null when no category was selected.
    /// </summary>
    public ListingCategory? Category { get; set; }

    public UserActivityType ActivityType { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public Listing? Listing { get; set; }
}
