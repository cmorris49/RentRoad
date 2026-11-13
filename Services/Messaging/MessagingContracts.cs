using RentRoad.Models.Enums;

namespace RentRoad.Services.Messaging;

/// <summary>
/// Contains only client-editable message text and its stable submission token.
/// </summary>
/// <param name="Content">The exact plain-text message content.</param>
/// <param name="ClientMessageId">The stable identifier used to recognize retries of the same message submission.</param>
public sealed record MessageSend(string? Content, Guid? ClientMessageId);

/// <summary>
/// Bounds history by message ID; before and after cursors are mutually exclusive.
/// </summary>
/// <param name="Limit">The maximum number of messages requested for the history page.</param>
/// <param name="BeforeMessageId">The optional exclusive upper message identifier for older history.</param>
/// <param name="AfterMessageId">The optional exclusive lower message identifier for newer history.</param>
public sealed record MessageHistoryParameters(
    int Limit = 30,
    int? BeforeMessageId = null,
    int? AfterMessageId = null)
{
    public const int DEFAULT_LIMIT = 30;
    public const int MAX_LIMIT = 100;
}

/// <summary>
/// Bounds one-based inbox pages without accepting a participant identifier.
/// </summary>
/// <param name="PageNumber">The requested one-based page number.</param>
/// <param name="PageSize">The number of records requested per page.</param>
public sealed record ConversationInboxParameters(int PageNumber = 1, int PageSize = 20)
{
    public const int DEFAULT_PAGE_SIZE = 20;
    public const int MAX_PAGE_SIZE = 100;
}

/// <summary>
/// References an image only through existing authorized delivery and never through a storage address.
/// </summary>
/// <param name="ListingImageId">The identifier of the listing image.</param>
/// <param name="AltText">The optional alternative text for the listing image.</param>
public sealed record ConversationImage(int ListingImageId, string? AltText);

/// <summary>
/// Contains safe listing and counterparty context, retaining private history without granting public listing/image
/// access.
/// </summary>
/// <param name="ConversationId">The conversation identifier, when supplied.</param>
/// <param name="ListingId">The identifier of the listing being processed.</param>
/// <param name="ListingTitle">The listing title, when supplied.</param>
/// <param name="ListingStatus">The listing's current lifecycle status.</param>
/// <param name="IsOwner">Whether the viewer is the listing owner in this conversation.</param>
/// <param name="CounterpartyDisplayName">The other participant's public display name.</param>
/// <param name="Image">The authorized image delivery reference, when available.</param>
/// <param name="CreatedAtUtc">The creation timestamp in UTC.</param>
public sealed record ConversationDetails(
    int ConversationId,
    int ListingId,
    string? ListingTitle,
    ListingStatus ListingStatus,
    bool IsOwner,
    string CounterpartyDisplayName,
    ConversationImage? Image,
    DateTime CreatedAtUtc)
{
    public bool IsListingPublic => ListingStatus == ListingStatus.Published;
}

/// <summary>
/// Describes a saved plain-text message using public sender display information; retry tokens are visible only to
/// their sender.
/// </summary>
/// <param name="MessageId">The identifier of the persisted message.</param>
/// <param name="ConversationId">The conversation identifier, when supplied.</param>
/// <param name="Content">The exact plain-text message content.</param>
/// <param name="SenderDisplayName">The message sender's public display name.</param>
/// <param name="IsMine">Whether the message was sent by the viewing participant.</param>
/// <param name="SentAtUtc">The message's persisted send timestamp in UTC.</param>
/// <param name="ReadAtUtc">The message's first read acknowledgment timestamp in UTC, when recorded.</param>
/// <param name="ClientMessageId">The stable identifier used to recognize retries of the same message submission.</param>
public sealed record MessageItem(
    int MessageId,
    int ConversationId,
    string Content,
    string SenderDisplayName,
    bool IsMine,
    DateTime SentAtUtc,
    DateTime? ReadAtUtc,
    Guid? ClientMessageId);

/// <summary>
/// Returns messages ascending by ID; NextCursor continues in the requested older/newer direction when HasMore is true.
/// </summary>
/// <param name="Messages">The messages included in this history page.</param>
/// <param name="HasMore">Whether another history page is available in the requested direction.</param>
/// <param name="NextCursor">The message identifier used to request the next history page, when available.</param>
public sealed record MessageHistoryPage(IReadOnlyList<MessageItem> Messages, bool HasMore, int? NextCursor);

/// <summary>
/// Contains inbox context, the latest persisted text preview and current received-unread count.
/// </summary>
/// <param name="Conversation">The listing and participant context for the conversation.</param>
/// <param name="LatestMessagePreview">The latest message preview, when a message exists.</param>
/// <param name="LastActivityAtUtc">The conversation's latest activity timestamp in UTC.</param>
/// <param name="UnreadCount">The number of received unread messages.</param>
public sealed record ConversationInboxItem(
    ConversationDetails Conversation,
    string? LatestMessagePreview,
    DateTime LastActivityAtUtc,
    int UnreadCount);

/// <summary>
/// Returns a deterministic bounded inbox page; concurrent activity may move conversations between pages.
/// </summary>
/// <param name="Items">The records included in this result page.</param>
/// <param name="TotalCount">The total number of matching records before pagination.</param>
/// <param name="PageNumber">The requested one-based page number.</param>
/// <param name="PageSize">The number of records requested per page.</param>
public sealed record ConversationInboxPage(
    IReadOnlyList<ConversationInboxItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize)
{
    /// <summary>
    /// Gets the page count without overflowing the item-count calculation.
    /// </summary>
    public int TotalPages => (int)(((long)TotalCount + PageSize - 1) / PageSize);
}

/// <summary>
/// Confirms the observed boundary and the number of newly acknowledged messages known from this attempt.
/// </summary>
/// <param name="ConversationId">The conversation identifier, when supplied.</param>
/// <param name="ThroughMessageId">The observed message identifier through which received messages are acknowledged.</param>
/// <param name="NewlyReadCount">The number of messages newly acknowledged by this operation.</param>
public sealed record MessageReadAcknowledgment(int ConversationId, int ThroughMessageId, int NewlyReadCount);
