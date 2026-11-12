using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RentRoad.Services.Messaging;

namespace RentRoad.Hubs;

/// <summary>Provides authorized ephemeral conversation subscriptions without creating messages, conversations or read receipts.</summary>
[Authorize]
public sealed class MessagesHub : Hub<IMessagesClient>
{
    public const string ROUTE = "/hubs/messages";
    public const int MAX_SUBSCRIPTIONS = 20;
    public const long MAX_RECEIVE_BYTES = 2048;
    private const string SUBSCRIPTIONS_KEY = "RentRoad.Messages.Subscriptions";
    private readonly IMessagingService messaging;

    /// <summary>Initializes subscriptions with the existing participant-authorizing messaging service.</summary>
    public MessagesHub(IMessagingService messaging) => this.messaging = messaging;

    /// <summary>Rejects connections without an authenticated Identity identifier matching SignalR's default user routing.</summary>
    public override Task OnConnectedAsync()
    {
        var id = Context.User?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(id) || !string.Equals(id, Context.UserIdentifier, StringComparison.Ordinal))
        {
            Context.Abort();
            return Task.CompletedTask;
        }
        return base.OnConnectedAsync();
    }

    /// <summary>Authorizes before joining a server-derived group, returning the same structured failure for missing and private conversations.</summary>
    [HubMethodName("JoinConversation")]
    public async Task<MessagingResult<int>> JoinConversationAsync(int conversationId)
    {
        var authorized = await messaging.GetConversationAsync(conversationId, Context.User, Context.ConnectionAborted);
        if (!authorized.IsSuccess || authorized.Value is null)
            return MessagingResult<int>.Failure(authorized.Status, authorized.Errors.ToArray());
        var id = authorized.Value.ConversationId;
        var subscriptions = Subscriptions();
        if (subscriptions.Contains(id)) return MessagingResult<int>.Success(id, alreadyApplied: true);
        if (subscriptions.Count >= MAX_SUBSCRIPTIONS) return MessagingResult<int>.Failure(MessagingServiceStatus.Conflict,
            new MessagingError(string.Empty, "Messages.SubscriptionLimit", MAX_SUBSCRIPTIONS));
        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(id), Context.ConnectionAborted);
        subscriptions.Add(id);
        return MessagingResult<int>.Success(id);
    }

    /// <summary>Leaves only this connection's previously authorized subscription; unknown IDs are harmless idempotent no-ops.</summary>
    [HubMethodName("LeaveConversation")]
    public async Task<MessagingResult<int>> LeaveConversationAsync(int conversationId)
    {
        if (conversationId <= 0) return MessagingResult<int>.Failure(MessagingServiceStatus.NotFound,
            new MessagingError(string.Empty, "Conversations.NotAvailable"));
        var subscriptions = Subscriptions();
        if (!subscriptions.Contains(conversationId)) return MessagingResult<int>.Success(conversationId, alreadyApplied: true);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroup(conversationId), Context.ConnectionAborted);
        subscriptions.Remove(conversationId);
        return MessagingResult<int>.Success(conversationId);
    }

    /// <summary>Derives a culture-independent routing name only from a positive, service-authorized conversation identifier.</summary>
    internal static string ConversationGroup(int conversationId) => "conversation:" + conversationId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Retains only bounded subscription IDs in SignalR's connection-local state, which is discarded at disconnection.</summary>
    private HashSet<int> Subscriptions()
    {
        if (Context.Items.TryGetValue(SUBSCRIPTIONS_KEY, out var value)) return (HashSet<int>)value!;
        var subscriptions = new HashSet<int>();
        Context.Items[SUBSCRIPTIONS_KEY] = subscriptions;
        return subscriptions;
    }
}
