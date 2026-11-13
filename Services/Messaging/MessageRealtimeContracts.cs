namespace RentRoad.Services.Messaging;

/// <summary>
/// Identifies a committed message that clients must fetch through authorized persisted history.
/// </summary>
/// <param name="ConversationId">The conversation identifier, when supplied.</param>
/// <param name="MessageId">The identifier of the persisted message.</param>
public sealed record MessageAvailableNotification(int ConversationId, int MessageId);

/// <summary>
/// Invalidates the two participants' inbox views without sending private content, counts or user identifiers.
/// </summary>
/// <param name="ConversationId">The conversation identifier, when supplied.</param>
/// <param name="MessageId">The identifier of the persisted message.</param>
public sealed record ConversationUpdatedNotification(int ConversationId, int MessageId);

/// <summary>
/// Defines stable outbound SignalR event names with minimal persisted-ID payloads.
/// </summary>
public interface IMessagesClient
{
    /// <summary>
    /// Notifies authorized conversation subscribers that persisted history may contain a message they have not fetched.
    /// </summary>
    /// <param name="notification">The persisted message and conversation identifiers used as a refresh hint.</param>
    /// <returns>A task representing completion of notification dispatch.</returns>
    Task MessageAvailable(MessageAvailableNotification notification);

    /// <summary>
    /// Notifies all connected devices of a participant that its authorized inbox should be refreshed.
    /// </summary>
    /// <param name="notification">The persisted message and conversation identifiers used as a refresh hint.</param>
    /// <returns>A task representing completion of notification dispatch.</returns>
    Task ConversationUpdated(ConversationUpdatedNotification notification);
}

/// <summary>
/// Distinguishes a skipped replay/failure, accepted dispatch and unavailable live delivery independently of message
/// persistence.
/// </summary>
public enum MessageRealtimeNotificationStatus
{
    Skipped,
    Dispatched,
    Unavailable
}
