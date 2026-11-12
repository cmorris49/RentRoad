using System.ComponentModel.DataAnnotations;

namespace RentRoad.Models;

public class Message
{
    public int MessageId { get; set; }

    public int ConversationId { get; set; }

    /// <summary>
    /// Gets or sets the stable submission identifier reused across send retries, or null for legacy messages.
    /// </summary>
    public Guid? ClientMessageId { get; set; }

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
