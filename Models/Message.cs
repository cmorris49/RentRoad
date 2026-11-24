using System.ComponentModel.DataAnnotations;
using RentRoad.Models.Enums;

namespace RentRoad.Models;

public class Message
{
    public int MessageId { get; set; }

    public int ConversationId { get; set; }

    /// <summary>
    /// Gets or sets the stable submission identifier reused across send retries, or null for legacy messages.
    /// </summary>
    public Guid? ClientMessageId { get; set; }

    /// <summary>Identifies the user author, or null for a trusted rental event.</summary>
    public string? SenderId { get; set; }

    /// <summary>Links a trusted request event to its immutable rental terms; ordinary sends cannot set this value.</summary>
    public int? RentalRequestId { get; set; }

    /// <summary>
    /// Gets or sets the trusted rental action; existing event rows retain the submission value of zero.
    /// </summary>
    public RentalMessageEventType RentalEventType { get; set; }

    public RentalRequest? RentalRequest { get; set; }

    /// <summary>Stores exact chat text, or the listing title snapshot for a trusted rental event.</summary>
    [Required]
    [StringLength(2000)]
    public string Content { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public ApplicationUser? Sender { get; set; }
}
