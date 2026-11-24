using Microsoft.EntityFrameworkCore;
using RentRoad.Data;
using RentRoad.Models;
using RentRoad.Models.Enums;
using RentRoad.Services.Messaging;

namespace RentRoad.Services.Rentals;

/// <summary>Persists trusted request events in the rental transaction and dispatches hints after commit.</summary>
public sealed partial class RentalRequestService
{
    /// <summary>Recognizes the original form submission independently of its current rental lifecycle status.</summary>
    private static async Task<RentalRequestServiceResult?> FindRequestRetryAsync(ApplicationDbContext database,
        string renterId, RentalRequestCreate details, CancellationToken cancellationToken)
    {
        if (details.ClientRequestId is not { } token || token == Guid.Empty)
        {
            return null;
        }

        var stored = await database.RentalRequests.AsNoTracking().SingleOrDefaultAsync(request =>
            request.RenterId == renterId && request.ClientRequestId == token, cancellationToken);

        if (stored is null)
        {
            return null;
        }

        return stored.ListingId == details.ListingId && stored.StartDate == details.StartDate && stored.EndDate == details.EndDate
            && stored.PickupTime == details.PickupTime && stored.ReturnTime == details.ReturnTime
            ? RentalRequestServiceResult.Success(stored, alreadyPending: stored.Status == RentalStatus.Pending, alreadyApplied: true)
            : Failure(RentalRequestServiceStatus.Conflict, string.Empty, "RentalEvent.RetryMismatch");
    }

    /// <summary>
    /// Saves the initial trusted submission event within the request creation transaction.
    /// </summary>
    /// <param name="database">The independent context participating in the rental transaction.</param>
    /// <param name="request">The newly added rental request and its verified terms.</param>
    /// <param name="cancellationToken">The token used to cancel persistence.</param>
    /// <returns>The saved message projection for post-commit dispatch.</returns>
    private static Task<MessageItem> AddRequestMessageAsync(
        ApplicationDbContext database,
        RentalRequest request,
        CancellationToken cancellationToken) =>
        AddRentalMessageAsync(database, request, RentalMessageEventType.RequestSubmitted,
            request.CreatedAtUtc, cancellationToken);

    /// <summary>
    /// Reuses the locked listing conversation and saves a trusted rental event within the caller's transaction.
    /// </summary>
    /// <param name="database">The independent context participating in the rental transaction.</param>
    /// <param name="request">The verified request whose immutable terms belong to this event.</param>
    /// <param name="eventType">The server-selected rental action being recorded.</param>
    /// <param name="sentAtUtc">The persisted action timestamp in UTC.</param>
    /// <param name="cancellationToken">The token used to cancel persistence.</param>
    /// <returns>The saved message projection for its initiating participant.</returns>
    private static async Task<MessageItem> AddRentalMessageAsync(
        ApplicationDbContext database,
        RentalRequest request,
        RentalMessageEventType eventType,
        DateTime sentAtUtc,
        CancellationToken cancellationToken)
    {
        await MessagingService.LockPairAsync(database, request.ListingId, request.RenterId, cancellationToken);
        var conversation = await database.Conversations.SingleOrDefaultAsync(
            item => item.ListingId == request.ListingId && item.RenterId == request.RenterId,
            cancellationToken);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                ListingId = request.ListingId,
                RenterId = request.RenterId,
                CreatedAtUtc = sentAtUtc
            };
            database.Conversations.Add(conversation);
        }
        else
        {
            await MessagingService.LockConversationAsync(
                database, conversation.ConversationId, request.RenterId, cancellationToken);
        }

        var title = await database.Listings.Where(item => item.ListingId == request.ListingId)
            .Select(item => item.Title).SingleAsync(cancellationToken);

        if (database.Entry(request).State == EntityState.Detached)
        {
            database.Attach(request);
        }

        var message = new Message
        {
            Conversation = conversation,
            RentalRequest = request,
            RentalEventType = eventType,
            Content = title ?? string.Empty,
            SentAtUtc = sentAtUtc
        };

        database.Messages.Add(message);
        await database.SaveChangesAsync(cancellationToken);

        return new(message.MessageId, conversation.ConversationId, message.Content, string.Empty, false,
            message.SentAtUtc, message.SentAtUtc, null)
        {
            RentalEvent = new(request.RentalRequestId, message.Content, request.StartDate, request.EndDate,
                request.TotalPriceCents, request.Status, eventType == RentalMessageEventType.RequestApproved, eventType)
        };
    }

    /// <summary>Leaves the committed result intact if best-effort live delivery is unavailable.</summary>
    private async Task NotifyRequestAsync(System.Security.Claims.ClaimsPrincipal? user, RentalRequestServiceResult result)
    {
        if (notifier is null || !result.IsSuccess || result.NotificationMessage is not { } message)
        {
            return;
        }

        try
        {
            await notifier.NotifyMessageSavedAsync(user, MessagingResult<MessageItem>.Success(message), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Rental event live delivery was unavailable ({FailureType}); persisted history remains authoritative.",
                exception.GetType().Name);
        }
    }
}
