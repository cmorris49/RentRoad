using System.Security.Claims;

namespace RentRoad.Services.Messaging;

/// <summary>
/// Persists private two-person listing conversations independently of HTTP and live delivery.
/// </summary>
public interface IMessagingService
{
    /// <summary>
    /// Starts a published listing conversation as the current renter, or resumes that renter's existing conversation.
    /// </summary>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<MessagingResult<ConversationDetails>> GetOrCreateForListingAsync(
        int listingId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves safe context only for the conversation's renter or immutable listing owner.
    /// </summary>
    /// <param name="conversationId">The conversation identifier, when supplied.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<MessagingResult<ConversationDetails>> GetConversationAsync(
        int conversationId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves server-only delivery recipients for a committed message authored by the current participant,
    /// outside caller transactions.
    /// </summary>
    /// <param name="conversationId">The conversation identifier, when supplied.</param>
    /// <param name="messageId">The identifier of the persisted message.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<MessagingResult<MessageNotificationRoute>> GetMessageNotificationRouteAsync(
        int conversationId,
        int messageId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits an exact plain-text submission or returns its previously saved message when the sender's token repeats.
    /// </summary>
    /// <param name="conversationId">The conversation identifier, when supplied.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="submission">The submitted exact message text and stable retry identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<MessagingResult<MessageItem>> SendAsync(
        int conversationId,
        ClaimsPrincipal? user,
        MessageSend? submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves bounded message-ID history, ascending within each page, with optional older or newer cursors.
    /// </summary>
    /// <param name="conversationId">The conversation identifier, when supplied.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="parameters">The history limit and optional directional message cursors.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<MessagingResult<MessageHistoryPage>> GetHistoryAsync(
        int conversationId,
        ClaimsPrincipal? user,
        MessageHistoryParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current account's inbox by latest activity with one-based bounded pagination.
    /// </summary>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="parameters">The requested one-based pagination settings.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<MessagingResult<ConversationInboxPage>> GetInboxAsync(
        ClaimsPrincipal? user,
        ConversationInboxParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts received unread messages for the current account, optionally scoped to one authorized conversation.
    /// </summary>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="conversationId">The conversation identifier, when supplied.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<MessagingResult<int>> GetUnreadCountAsync(
        ClaimsPrincipal? user,
        int? conversationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges only unread received messages through an observed message in this conversation, preserving
    /// earlier receipts.
    /// </summary>
    /// <param name="conversationId">The conversation identifier, when supplied.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="throughMessageId">The observed message identifier through which received messages are acknowledged.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<MessagingResult<MessageReadAcknowledgment>> MarkReadThroughAsync(
        int conversationId,
        ClaimsPrincipal? user,
        int throughMessageId,
        CancellationToken cancellationToken = default);
}
