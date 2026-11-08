using System.ComponentModel.DataAnnotations;

namespace RentRoad.Models;

public class Message
{
    public int MessageId { get; set; }

    public int ConversationId { get; set; }

    [Required]
    public string SenderId { get; set; } = string.Empty;

    [Required]
    [StringLength(2000)]
    public string Content { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public ApplicationUser Sender { get; set; } = null!;
}
