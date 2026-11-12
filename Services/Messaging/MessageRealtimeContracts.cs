namespace RentRoad.Services.Messaging;

/// <summary>Identifies a committed message that clients must fetch through authorized persisted history.</summary>
public sealed record MessageAvailableNotification(int ConversationId, int MessageId);

/// <summary>Invalidates the two participants' inbox views without sending private content, counts or user identifiers.</summary>
public sealed record ConversationUpdatedNotification(int ConversationId, int MessageId);

/// <summary>Defines stable outbound SignalR event names with minimal persisted-ID payloads.</summary>
public interface IMessagesClient
{
    /// <summary>Notifies authorized conversation subscribers that persisted history may contain a message they have not fetched.</summary>
    Task MessageAvailable(MessageAvailableNotification notification);

    /// <summary>Notifies all connected devices of a participant that its authorized inbox should be refreshed.</summary>
    Task ConversationUpdated(ConversationUpdatedNotification notification);
}

/// <summary>Distinguishes a skipped replay/failure, accepted dispatch and unavailable live delivery independently of message persistence.</summary>
public enum MessageRealtimeNotificationStatus
{
    Skipped,
    Dispatched,
    Unavailable
}
