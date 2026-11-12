using System.ComponentModel.DataAnnotations;
using RentRoad.Models;
using RentRoad.Models.Enums;
using RentRoad.Services.Messaging;

namespace RentRoad.ViewModels.Messaging;

/// <summary>Supplies retained listing/counterparty context without private Identity keys or storage addresses.</summary>
public sealed class ConversationContextViewModel
{
    public int ConversationId { get; }
    public int ListingId { get; }
    public string ListingTitle { get; }
    public ListingStatus ListingStatus { get; }
    public bool IsListingPublic { get; }
    public bool IsOwner { get; }
    public string CounterpartyDisplayName { get; }
    public ConversationImage? Image { get; }
    public DateTime CreatedAtUtc { get; }

    /// <summary>Maps only the existing safe context projection, retaining image-access hints and a localized untitled fallback.</summary>
    public ConversationContextViewModel(ConversationDetails details)
    {
        ConversationId = details.ConversationId;
        ListingId = details.ListingId;
        ListingTitle = string.IsNullOrWhiteSpace(details.ListingTitle) ? SharedResource.Get("MyListings.Untitled") : details.ListingTitle;
        ListingStatus = details.ListingStatus;
        IsListingPublic = details.IsListingPublic;
        IsOwner = details.IsOwner;
        CounterpartyDisplayName = details.CounterpartyDisplayName;
        Image = details.Image;
        CreatedAtUtc = details.CreatedAtUtc;
    }
}

/// <summary>Provides persisted plain text, public sender display and read state; only the sender receives its retry token.</summary>
public sealed record MessageViewModel(int MessageId, int ConversationId, string Content, string SenderDisplayName, bool IsMine,
    DateTime SentAtUtc, DateTime? ReadAtUtc, Guid? ClientMessageId)
{
    public bool IsRead => ReadAtUtc.HasValue;

    /// <summary>Preserves exact service-projected message values without interpreting markup or exposing sender account keys.</summary>
    public static MessageViewModel FromResult(MessageItem message) => new(message.MessageId, message.ConversationId, message.Content,
        message.SenderDisplayName, message.IsMine, message.SentAtUtc, message.ReadAtUtc, message.ClientMessageId);
}

/// <summary>Retains bounded ascending message display order and the service's directional keyset cursor.</summary>
public sealed record MessageHistoryViewModel(IReadOnlyList<MessageViewModel> Messages, bool HasMore, int? NextCursor)
{
    /// <summary>Maps the existing history page without querying, reordering or acknowledging its messages.</summary>
    public static MessageHistoryViewModel FromResult(MessageHistoryPage page) => new(page.Messages.Select(MessageViewModel.FromResult).ToArray(),
        page.HasMore, page.NextCursor);
}

/// <summary>Displays one authorized inbox row with a bounded latest preview and received-unread count.</summary>
public sealed record ConversationInboxItemViewModel(ConversationContextViewModel Conversation, string? LatestMessagePreview,
    DateTime LastActivityAtUtc, int UnreadCount);

/// <summary>Prepares the future inbox Razor View while preserving service-defined scoping, ordering and pagination.</summary>
public sealed class MessagingInboxViewModel
{
    public IReadOnlyList<ConversationInboxItemViewModel> Conversations { get; }
    public int TotalCount { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public int TotalPages { get; }
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>Maps only safe inbox projections; empty pages remain successful empty pages.</summary>
    public MessagingInboxViewModel(ConversationInboxPage page)
    {
        Conversations = page.Items.Select(item => new ConversationInboxItemViewModel(new(item.Conversation), item.LatestMessagePreview,
            item.LastActivityAtUtc, item.UnreadCount)).ToArray();
        TotalCount = page.TotalCount;
        PageNumber = page.PageNumber;
        PageSize = page.PageSize;
        TotalPages = page.TotalPages;
    }
}

/// <summary>Provides only public hub protocol identifiers; no connection token, group name or participant IDs are sent.</summary>
public sealed record MessagingConnectionViewModel(string HubUrl)
{
    public string JoinMethod => "JoinConversation";
    public string LeaveMethod => "LeaveConversation";
    public string MessageAvailableEvent => nameof(IMessagesClient.MessageAvailable);
    public string ConversationUpdatedEvent => nameof(IMessagesClient.ConversationUpdated);
}

/// <summary>Provides application-generated local HTTP addresses for the future authenticated chat client.</summary>
public sealed record MessagingEndpointsViewModel(string HistoryUrl, string SendUrl, string ReadUrl, string UnreadCountUrl);

/// <summary>Prepares safe conversation context, initial history and transport metadata without any read-state side effect.</summary>
public sealed record ConversationViewModel(ConversationContextViewModel Conversation, MessageHistoryViewModel History,
    MessagingConnectionViewModel Realtime, MessagingEndpointsViewModel Endpoints, string? AntiforgeryHeaderName)
{
    public MessageSendInputViewModel SendInput { get; } = new();

    /// <summary>Supplies the authorized bounded sidebar page prepared by the conversation action.</summary>
    public MessagingInboxViewModel? Inbox { get; init; }

    /// <summary>Exposes existing model metadata for composer guidance without changing server validation.</summary>
    public int MaximumContentLength => ((StringLengthAttribute)Attribute.GetCustomAttribute(
        typeof(Message).GetProperty(nameof(Message.Content))!, typeof(StringLengthAttribute))!).MaximumLength;
}

/// <summary>Identifies the optional conversation scope and its authoritative received-unread count.</summary>
public sealed record MessagingUnreadCountViewModel(int? ConversationId, int UnreadCount);

/// <summary>Confirms the explicit read boundary and number of newly acknowledged messages known to the service.</summary>
public sealed record MessageReadViewModel(int ConversationId, int ThroughMessageId, int NewlyReadCount);

/// <summary>Shares the paginated inbox and optional authorized active conversation between full-page and enhanced navigation.</summary>
public sealed record MessagingWorkspaceViewModel(MessagingInboxViewModel Inbox, ConversationViewModel? ActiveConversation);
