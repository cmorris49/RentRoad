using System.Security.Claims;

namespace RentRoad.Services.Messaging;

/// <summary>Dispatches best-effort live hints only after an application boundary receives a confirmed new-message save result.</summary>
public interface IMessageRealtimeNotifier
{
    /// <summary>Accepts a trusted server-side SendAsync result, skips failed/idempotent outcomes, and never changes the saved-message outcome on delivery failure.</summary>
    Task<MessageRealtimeNotificationStatus> NotifyMessageSavedAsync(ClaimsPrincipal? user, MessagingResult<MessageItem>? submission,
        CancellationToken cancellationToken = default);
}
