using System.ComponentModel.DataAnnotations;

namespace RentRoad.Models;

public class Conversation
{
    public int ConversationId { get; set; }

    public int ListingId { get; set; }

    [Required]
    public string RenterId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public Listing Listing { get; set; } = null!;

    public ApplicationUser Renter { get; set; } = null!;

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
