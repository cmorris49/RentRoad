using RentRoad.Services.Messaging;

namespace RentRoad.ViewModels.Messaging;

/// <summary>Binds only the listing used to start or resume the authenticated renter's conversation.</summary>
public sealed class ConversationStartInputViewModel
{
    public int ListingId { get; set; }
}

/// <summary>Binds exact plain-text content and the client's stable retry token, excluding sender, ownership and timestamps.</summary>
public sealed class MessageSendInputViewModel
{
    public string? Content { get; set; }
    public Guid? ClientMessageId { get; set; }

    /// <summary>Preserves submitted text and the supplied token without generating or normalizing either value.</summary>
    public MessageSend ToSubmission() => new(Content, ClientMessageId);
}

/// <summary>Binds only the message boundary the current participant explicitly acknowledges.</summary>
public sealed class MessageReadInputViewModel
{
    public int ThroughMessageId { get; set; }
}
