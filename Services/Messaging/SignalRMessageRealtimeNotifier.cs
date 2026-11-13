using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using RentRoad.Hubs;

namespace RentRoad.Services.Messaging;

/// <summary>
/// Routes minimal post-commit events through SignalR without making live delivery part of the SQL transaction.
/// </summary>
public sealed class SignalRMessageRealtimeNotifier : IMessageRealtimeNotifier
{
    private const int DISPATCH_TIMEOUT_SECONDS = 5;
    private readonly IMessagingService messaging;
    private readonly IHubContext<MessagesHub, IMessagesClient> hub;
    private readonly ILogger<SignalRMessageRealtimeNotifier> logger;

    /// <summary>
    /// Initializes delivery using verified service routing, SignalR's hub context and sanitized technical diagnostics.
    /// </summary>
    /// <param name="messaging">The messaging service used to verify persisted notification routing.</param>
    /// <param name="hub">The SignalR hub context used to notify authorized clients.</param>
    /// <param name="logger">The logger used to record operational failures.</param>
    public SignalRMessageRealtimeNotifier(
        IMessagingService messaging,
        IHubContext<MessagesHub, IMessagesClient> hub,
        ILogger<SignalRMessageRealtimeNotifier> logger)
    {
        this.messaging = messaging;
        this.hub = hub;
        this.logger = logger;
    }

    /// <summary>
    /// Dispatches only confirmed new messages, independently notifying subscribers and both participants' connected
    /// devices.
    /// </summary>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="submission">The trusted message-save result supplied for post-commit notification.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the notification outcome independently of message persistence.</returns>
    public async Task<MessageRealtimeNotificationStatus> NotifyMessageSavedAsync(
        ClaimsPrincipal? user,
        MessagingResult<MessageItem>? submission,
        CancellationToken cancellationToken = default)
    {
        if (submission is not { IsSuccess: true, AlreadyApplied: false, Value: { IsMine: true } saved }
            || saved.ConversationId <= 0
            || saved.MessageId <= 0)
        {
            return MessageRealtimeNotificationStatus.Skipped;
        }

        try
        {
            using var delivery = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            delivery.CancelAfter(TimeSpan.FromSeconds(DISPATCH_TIMEOUT_SECONDS));
            var authorized = await messaging.GetMessageNotificationRouteAsync(
                saved.ConversationId,
                saved.MessageId,
                user,
                delivery.Token);

            if (!authorized.IsSuccess || authorized.Value is null)
            {
                logger.LogWarning("Message live delivery routing was unavailable ({Status}).", authorized.Status);
                return MessageRealtimeNotificationStatus.Unavailable;
            }

            var route = authorized.Value;
            var conversation = NotifyConversationAsync(route);
            var inboxes = NotifyParticipantsAsync(route);

            await Task.WhenAll(conversation, inboxes).WaitAsync(delivery.Token);
            return MessageRealtimeNotificationStatus.Dispatched;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Message live delivery could not be confirmed ({FailureType}); persisted history remains authoritative.",
                exception.GetType().Name);
            return MessageRealtimeNotificationStatus.Unavailable;
        }
    }

    /// <summary>
    /// Attempts conversation delivery independently so a synchronous channel failure cannot prevent inbox dispatch.
    /// </summary>
    /// <param name="route">The verified persisted message route and participant identifiers.</param>
    /// <returns>A task representing completion of notification dispatch.</returns>
    private async Task NotifyConversationAsync(MessageNotificationRoute route) =>
        await hub.Clients.Group(MessagesHub.ConversationGroup(route.ConversationId))
            .MessageAvailable(new(route.ConversationId, route.MessageId));

    /// <summary>
    /// Targets every connected device of the two database-derived participants without requiring group membership.
    /// </summary>
    /// <param name="route">The verified persisted message route and participant identifiers.</param>
    /// <returns>A task representing completion of notification dispatch.</returns>
    private async Task NotifyParticipantsAsync(MessageNotificationRoute route) =>
        await hub.Clients.Users(new[] { route.RenterId, route.OwnerId })
            .ConversationUpdated(new(route.ConversationId, route.MessageId));
}
