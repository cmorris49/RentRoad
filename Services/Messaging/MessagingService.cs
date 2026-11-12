using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RentRoad.Data;
using RentRoad.Models;
using RentRoad.Models.Enums;

namespace RentRoad.Services.Messaging;

/// <summary>Enforces two-participant messaging, stable submission identities and explicit read acknowledgments.</summary>
public sealed partial class MessagingService : IMessagingService
{
    private const string CONVERSATION_UNIQUE_INDEX = "IX_Conversations_ListingId_RenterId";
    private const string MESSAGE_UNIQUE_INDEX = "IX_Messages_ConversationId_SenderId_ClientMessageId";
    private const int PREVIEW_LENGTH = 160;
    private readonly ApplicationDbContext context;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<MessagingService> logger;

    /// <summary>Initializes messaging with the existing relational context, UTC clock and sanitized diagnostic logger.</summary>
    public MessagingService(ApplicationDbContext context, TimeProvider timeProvider, ILogger<MessagingService> logger)
    {
        this.context = context;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Resumes a renter's retained pair, or creates one only for a published listing owned by someone else.</summary>
    public Task<MessagingResult<ConversationDetails>> GetOrCreateForListingAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default) => WriteAsync(user, async (database, actorId, token) =>
        {
            if (listingId <= 0) return Failure<ConversationDetails>(MessagingServiceStatus.NotFound, "Conversations.ListingNotAvailable");
            await LockPairAsync(database, listingId, actorId, token);
            var existing = await ConversationProjection(ParticipatingConversations(database, actorId)
                .Where(conversation => conversation.ListingId == listingId && conversation.RenterId == actorId), actorId).SingleOrDefaultAsync(token);
            if (existing is not null) return MessagingResult<ConversationDetails>.Success(existing, alreadyExists: true);
            var listing = await database.Listings.AsNoTracking().Where(item => item.ListingId == listingId
                    && (item.Status == ListingStatus.Published || item.OwnerId == actorId))
                .Select(item => new { item.OwnerId, item.Status }).SingleOrDefaultAsync(token);
            if (listing?.OwnerId == actorId) return Failure<ConversationDetails>(MessagingServiceStatus.Forbidden, "Conversations.SelfConversationForbidden");
            if (listing?.Status != ListingStatus.Published) return Failure<ConversationDetails>(MessagingServiceStatus.NotFound, "Conversations.ListingNotAvailable");
            var conversation = new Conversation { ListingId = listingId, RenterId = actorId, CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime };
            database.Conversations.Add(conversation);
            await database.SaveChangesAsync(token);
            var saved = await ConversationProjection(ParticipatingConversations(database, actorId)
                .Where(item => item.ConversationId == conversation.ConversationId), actorId).SingleAsync(token);
            return MessagingResult<ConversationDetails>.Success(saved);
        }, async (database, actorId, token) =>
        {
            var stored = await ConversationProjection(ParticipatingConversations(database, actorId)
                .Where(conversation => conversation.ListingId == listingId && conversation.RenterId == actorId), actorId).SingleOrDefaultAsync(token);
            return stored is null ? null : MessagingResult<ConversationDetails>.Success(stored, alreadyExists: true);
        }, CONVERSATION_UNIQUE_INDEX, "StartConversation", cancellationToken);

    /// <summary>Stores text exactly as supplied, using the current Identity sender and the same client token on every retry.</summary>
    public Task<MessagingResult<MessageItem>> SendAsync(int conversationId, ClaimsPrincipal? user, MessageSend? submission,
        CancellationToken cancellationToken = default) => WriteAsync(user, async (database, actorId, token) =>
        {
            if (!await LockConversationAsync(database, conversationId, actorId, token)) return Unavailable<MessageItem>();
            var errors = ValidateSubmission(submission);
            if (errors.Count > 0) return MessagingResult<MessageItem>.Failure(MessagingServiceStatus.ValidationFailed, errors.ToArray());
            var existing = await FindSubmissionAsync(database, conversationId, actorId, submission!.ClientMessageId!.Value, token);
            if (existing is not null) return RepeatedSubmission(existing, submission.Content!);
            var message = new Message
            {
                ConversationId = conversationId, SenderId = actorId, Content = submission.Content!, ClientMessageId = submission.ClientMessageId,
                SentAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };
            database.Messages.Add(message);
            await database.SaveChangesAsync(token);
            var saved = await database.Messages.AsNoTracking().Where(item => item.MessageId == message.MessageId)
                .Select(MessageProjection(actorId)).SingleAsync(token);
            return MessagingResult<MessageItem>.Success(saved);
        }, async (database, actorId, token) =>
        {
            if (submission?.ClientMessageId is not { } id || id == Guid.Empty) return null;
            var stored = await FindSubmissionAsync(database, conversationId, actorId, id, token);
            return stored is null ? null : RepeatedSubmission(stored, submission.Content!);
        }, MESSAGE_UNIQUE_INDEX, "SendMessage", cancellationToken);

    /// <summary>Marks only received unread rows through a valid observed message ID, never advancing to later arrivals.</summary>
    public Task<MessagingResult<MessageReadAcknowledgment>> MarkReadThroughAsync(int conversationId, ClaimsPrincipal? user, int throughMessageId,
        CancellationToken cancellationToken = default) => WriteAsync(user, async (database, actorId, token) =>
        {
            if (!await LockConversationAsync(database, conversationId, actorId, token)) return Unavailable<MessageReadAcknowledgment>();
            if (throughMessageId <= 0 || !await database.Messages.AsNoTracking().AnyAsync(message => message.ConversationId == conversationId
                && message.MessageId == throughMessageId, token))
                return Failure<MessageReadAcknowledgment>(MessagingServiceStatus.ValidationFailed, "Messages.InvalidCursor", nameof(MessageReadAcknowledgment.ThroughMessageId));
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var changed = await ReceivedUnread(database, actorId).Where(message => message.ConversationId == conversationId && message.MessageId <= throughMessageId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.ReadAtUtc, (DateTime?)now), token);
            return MessagingResult<MessageReadAcknowledgment>.Success(new(conversationId, throughMessageId, changed), alreadyApplied: changed == 0);
        }, async (database, actorId, token) =>
        {
            if (!await ParticipatingConversations(database, actorId).AnyAsync(conversation => conversation.ConversationId == conversationId, token)
                || !await database.Messages.AsNoTracking().AnyAsync(message => message.ConversationId == conversationId && message.MessageId == throughMessageId, token)
                || await ReceivedUnread(database, actorId).AnyAsync(message => message.ConversationId == conversationId && message.MessageId <= throughMessageId, token)) return null;
            return MessagingResult<MessageReadAcknowledgment>.Success(new(conversationId, throughMessageId, 0), alreadyApplied: true);
        }, null, "MarkRead", cancellationToken);

    /// <summary>Validates against the existing model length while leaving valid whitespace, newlines and user-authored text unchanged.</summary>
    private List<MessagingError> ValidateSubmission(MessageSend? submission)
    {
        var errors = new List<MessagingError>();
        var maxLength = context.Model.FindEntityType(typeof(Message))!.FindProperty(nameof(Message.Content))!.GetMaxLength()!.Value;
        if (submission?.ClientMessageId is null || submission.ClientMessageId == Guid.Empty)
            errors.Add(new(nameof(MessageSend.ClientMessageId), "Messages.InvalidClientMessageId"));
        if (string.IsNullOrWhiteSpace(submission?.Content)) errors.Add(new(nameof(MessageSend.Content), "Messages.InvalidContent"));
        else if (submission.Content.Length > maxLength)
            errors.Add(new(nameof(MessageSend.Content), "Messages.ContentTooLong", maxLength));
        return errors;
    }

    /// <summary>Distinguishes exact ordinal-text replay from token reuse with different content, preserving the stored message.</summary>
    private static MessagingResult<MessageItem> RepeatedSubmission(MessageItem stored, string content) =>
        string.Equals(stored.Content, content, StringComparison.Ordinal)
            ? MessagingResult<MessageItem>.Success(stored, alreadyApplied: true)
            : Failure<MessageItem>(MessagingServiceStatus.Conflict, "Messages.DuplicateTokenConflict", nameof(MessageSend.ClientMessageId));
}
