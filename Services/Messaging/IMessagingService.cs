using System.Security.Claims;

namespace RentRoad.Services.Messaging;

/// <summary>Persists private two-person listing conversations independently of HTTP and live delivery.</summary>
public interface IMessagingService
{
    /// <summary>Starts a published listing conversation as the current renter, or resumes that renter's existing conversation.</summary>
    Task<MessagingResult<ConversationDetails>> GetOrCreateForListingAsync(int listingId, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>Retrieves safe context only for the conversation's renter or immutable listing owner.</summary>
    Task<MessagingResult<ConversationDetails>> GetConversationAsync(int conversationId, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>Resolves server-only delivery recipients for a committed message authored by the current participant, outside caller transactions.</summary>
    Task<MessagingResult<MessageNotificationRoute>> GetMessageNotificationRouteAsync(int conversationId, int messageId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>Commits an exact plain-text submission or returns its previously saved message when the sender's token repeats.</summary>
    Task<MessagingResult<MessageItem>> SendAsync(int conversationId, ClaimsPrincipal? user, MessageSend? submission, CancellationToken cancellationToken = default);

    /// <summary>Retrieves bounded message-ID history, ascending within each page, with optional older or newer cursors.</summary>
    Task<MessagingResult<MessageHistoryPage>> GetHistoryAsync(int conversationId, ClaimsPrincipal? user, MessageHistoryParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves the current account's inbox by latest activity with one-based bounded pagination.</summary>
    Task<MessagingResult<ConversationInboxPage>> GetInboxAsync(ClaimsPrincipal? user, ConversationInboxParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Counts received unread messages for the current account, optionally scoped to one authorized conversation.</summary>
    Task<MessagingResult<int>> GetUnreadCountAsync(ClaimsPrincipal? user, int? conversationId = null, CancellationToken cancellationToken = default);

    /// <summary>Acknowledges only unread received messages through an observed message in this conversation, preserving earlier receipts.</summary>
    Task<MessagingResult<MessageReadAcknowledgment>> MarkReadThroughAsync(int conversationId, ClaimsPrincipal? user, int throughMessageId,
        CancellationToken cancellationToken = default);
}
