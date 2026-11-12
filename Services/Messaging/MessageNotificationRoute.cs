namespace RentRoad.Services.Messaging;

/// <summary>Contains verified persisted message IDs and assembly-private delivery recipients; this routing value is never an endpoint response.</summary>
public sealed class MessageNotificationRoute
{
    public int ConversationId { get; }
    public int MessageId { get; }
    internal string RenterId { get; }
    internal string OwnerId { get; }

    /// <summary>Creates a routing value only inside the application from authorized persisted relationships.</summary>
    internal MessageNotificationRoute(int conversationId, int messageId, string renterId, string ownerId)
    {
        ConversationId = conversationId;
        MessageId = messageId;
        RenterId = renterId;
        OwnerId = ownerId;
    }
}
