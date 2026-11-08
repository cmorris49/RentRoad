using System.ComponentModel.DataAnnotations;
using RentRoad.Models.Enums;

namespace RentRoad.Models;

/// <summary>
/// Represents an owner's item, including incomplete drafts and archived rental history.
/// </summary>
/// <remarks>
/// Services must keep ownership immutable and archive listings with rental history instead of deleting them.
/// Publication requires a title, description, positive price, category, condition, pickup area, images,
/// and at least one valid availability window. Without availability windows, an item cannot be rented.
/// Adjacent windows may collectively cover a rental period; blocks and approved or active rentals exclude dates.
/// Cross-record availability checks and publication validation belong in services.
/// </remarks>
public class Listing
{
    public int ListingId { get; set; }

    [Required]
    public string OwnerId { get; set; } = string.Empty;

    [StringLength(150)]
    public string? Title { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    public ListingCategory? Category { get; set; }

    public ItemCondition? Condition { get; set; }

    public long? DailyPriceCents { get; set; }

    [StringLength(150)]
    public string? PickupArea { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public ListingStatus Status { get; set; } = ListingStatus.Draft;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public ApplicationUser Owner { get; set; } = null!;

    public ICollection<ListingImage> Images { get; set; } = new List<ListingImage>();

    public ICollection<AvailabilityWindow> AvailabilityWindows { get; set; } = new List<AvailabilityWindow>();

    public ICollection<AvailabilityBlock> AvailabilityBlocks { get; set; } = new List<AvailabilityBlock>();

    public ICollection<RentalRequest> RentalRequests { get; set; } = new List<RentalRequest>();

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

    public ICollection<UserActivity> Activities { get; set; } = new List<UserActivity>();
}
