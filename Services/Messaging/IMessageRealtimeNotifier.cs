using System.Security.Claims;

namespace RentRoad.Services.Messaging;

/// <summary>
/// Dispatches best-effort live hints only after an application boundary receives a confirmed new-message save result.
/// </summary>
public interface IMessageRealtimeNotifier
{
    /// <summary>
    /// Accepts a trusted server-side SendAsync result, skips failed/idempotent outcomes, and never changes the
    /// saved-message outcome on delivery failure.
    /// </summary>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="submission">The trusted message-save result supplied for post-commit notification.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the notification outcome independently of message persistence.</returns>
    Task<MessageRealtimeNotificationStatus> NotifyMessageSavedAsync(
        ClaimsPrincipal? user,
        MessagingResult<MessageItem>? submission,
        CancellationToken cancellationToken = default);
}
