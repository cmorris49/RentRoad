using RentRoad.Models.Enums;

namespace RentRoad.Services.Messaging;

/// <summary>Contains only client-editable message text and its stable submission token.</summary>
public sealed record MessageSend(string? Content, Guid? ClientMessageId);

/// <summary>Bounds history by message ID; before and after cursors are mutually exclusive.</summary>
public sealed record MessageHistoryParameters(int Limit = 30, int? BeforeMessageId = null, int? AfterMessageId = null)
{
    public const int DEFAULT_LIMIT = 30;
    public const int MAX_LIMIT = 100;
}

/// <summary>Bounds one-based inbox pages without accepting a participant identifier.</summary>
public sealed record ConversationInboxParameters(int PageNumber = 1, int PageSize = 20)
{
    public const int DEFAULT_PAGE_SIZE = 20;
    public const int MAX_PAGE_SIZE = 100;
}

/// <summary>References an image only through existing authorized delivery and never through a storage address.</summary>
public sealed record ConversationImage(int ListingImageId, string? AltText);

/// <summary>Contains safe listing and counterparty context, retaining private history without granting public listing/image access.</summary>
public sealed record ConversationDetails(int ConversationId, int ListingId, string? ListingTitle, ListingStatus ListingStatus,
    bool IsOwner, string CounterpartyDisplayName, ConversationImage? Image, DateTime CreatedAtUtc)
{
    public bool IsListingPublic => ListingStatus == ListingStatus.Published;
}

/// <summary>Describes a saved plain-text message using public sender display information; retry tokens are visible only to their sender.</summary>
public sealed record MessageItem(int MessageId, int ConversationId, string Content, string SenderDisplayName, bool IsMine,
    DateTime SentAtUtc, DateTime? ReadAtUtc, Guid? ClientMessageId);

/// <summary>Returns messages ascending by ID; NextCursor continues in the requested older/newer direction when HasMore is true.</summary>
public sealed record MessageHistoryPage(IReadOnlyList<MessageItem> Messages, bool HasMore, int? NextCursor);

/// <summary>Contains inbox context, the latest persisted text preview and current received-unread count.</summary>
public sealed record ConversationInboxItem(ConversationDetails Conversation, string? LatestMessagePreview,
    DateTime LastActivityAtUtc, int UnreadCount);

/// <summary>Returns a deterministic bounded inbox page; concurrent activity may move conversations between pages.</summary>
public sealed record ConversationInboxPage(IReadOnlyList<ConversationInboxItem> Items, int TotalCount, int PageNumber, int PageSize)
{
    /// <summary>Gets the page count without overflowing the item-count calculation.</summary>
    public int TotalPages => (int)(((long)TotalCount + PageSize - 1) / PageSize);
}

/// <summary>Confirms the observed boundary and the number of newly acknowledged messages known from this attempt.</summary>
public sealed record MessageReadAcknowledgment(int ConversationId, int ThroughMessageId, int NewlyReadCount);
