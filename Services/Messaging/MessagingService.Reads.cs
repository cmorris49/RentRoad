using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RentRoad.Models;
using RentRoad.Models.Enums;

namespace RentRoad.Services.Messaging;

public sealed partial class MessagingService
{
    /// <summary>Reads delivery routing from persisted relationships only for this authenticated sender, refusing uncommitted caller work.</summary>
    public Task<MessagingResult<MessageNotificationRoute>> GetMessageNotificationRouteAsync(int conversationId, int messageId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default) => ReadAsync(user, async (database, actorId, token) =>
        {
            if (database.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                return Failure<MessageNotificationRoute>(MessagingServiceStatus.Conflict, "Messages.TransactionInProgress");
            await using var committed = FreshContext();
            var route = await ParticipatingConversations(committed, actorId).Where(conversation => conversation.ConversationId == conversationId)
                .Join(committed.Messages.AsNoTracking().Where(message => message.MessageId == messageId && message.SenderId == actorId),
                    conversation => conversation.ConversationId, message => message.ConversationId,
                    (conversation, message) => new MessageNotificationRoute(conversation.ConversationId, message.MessageId, conversation.RenterId, conversation.Listing.OwnerId))
                .SingleOrDefaultAsync(token);
            return route is null ? Unavailable<MessageNotificationRoute>() : MessagingResult<MessageNotificationRoute>.Success(route);
        }, cancellationToken);

    /// <summary>Reads participant-scoped retained context without requiring a publicly visible listing.</summary>
    public Task<MessagingResult<ConversationDetails>> GetConversationAsync(int conversationId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default) => ReadAsync(user, async (database, actorId, token) =>
        {
            var result = await ConversationProjection(ParticipatingConversations(database, actorId)
                .Where(conversation => conversation.ConversationId == conversationId), actorId).SingleOrDefaultAsync(token);
            return result is null ? Unavailable<ConversationDetails>() : MessagingResult<ConversationDetails>.Success(result);
        }, cancellationToken);

    /// <summary>Uses keyset cursors and message IDs as the canonical per-conversation sequence, not a client or wall-clock timestamp.</summary>
    public Task<MessagingResult<MessageHistoryPage>> GetHistoryAsync(int conversationId, ClaimsPrincipal? user, MessageHistoryParameters? parameters = null,
        CancellationToken cancellationToken = default) => ReadAsync(user, async (database, actorId, token) =>
        {
            if (!await ParticipatingConversations(database, actorId).AnyAsync(conversation => conversation.ConversationId == conversationId, token))
                return Unavailable<MessageHistoryPage>();
            var page = parameters ?? new();
            if (page.Limit < 1 || page.Limit > MessageHistoryParameters.MAX_LIMIT || page.BeforeMessageId <= 0 || page.AfterMessageId <= 0
                || page.BeforeMessageId.HasValue && page.AfterMessageId.HasValue)
                return Failure<MessageHistoryPage>(MessagingServiceStatus.ValidationFailed, "Messages.InvalidHistoryPagination", "", MessageHistoryParameters.MAX_LIMIT);
            var cursor = page.BeforeMessageId ?? page.AfterMessageId;
            if (cursor.HasValue && !await database.Messages.AsNoTracking().AnyAsync(message => message.ConversationId == conversationId && message.MessageId == cursor, token))
                return Failure<MessageHistoryPage>(MessagingServiceStatus.ValidationFailed, "Messages.InvalidCursor");
            var query = database.Messages.AsNoTracking().Where(message => message.ConversationId == conversationId);
            if (page.BeforeMessageId.HasValue) query = query.Where(message => message.MessageId < page.BeforeMessageId.Value);
            if (page.AfterMessageId.HasValue) query = query.Where(message => message.MessageId > page.AfterMessageId.Value);
            var ordered = page.AfterMessageId.HasValue ? query.OrderBy(message => message.MessageId) : query.OrderByDescending(message => message.MessageId);
            var messages = await ordered.Take(page.Limit + 1).Select(MessageProjection(actorId)).ToListAsync(token);
            var more = messages.Count > page.Limit;
            if (more) messages.RemoveAt(messages.Count - 1);
            if (!page.AfterMessageId.HasValue) messages.Reverse();
            var next = more ? (int?)(page.AfterMessageId.HasValue ? messages[^1].MessageId : messages[0].MessageId) : null;
            return MessagingResult<MessageHistoryPage>.Success(new(messages, more, next));
        }, cancellationToken);

    /// <summary>Projects a fixed number of participant-scoped rows and correlated latest/unread/image values without navigation graph loads.</summary>
    public Task<MessagingResult<ConversationInboxPage>> GetInboxAsync(ClaimsPrincipal? user, ConversationInboxParameters? parameters = null,
        CancellationToken cancellationToken = default) => ReadAsync(user, async (database, actorId, token) =>
        {
            var page = parameters ?? new();
            if (page.PageNumber < 1 || page.PageSize < 1 || page.PageSize > ConversationInboxParameters.MAX_PAGE_SIZE
                || ((long)page.PageNumber - 1) * page.PageSize > int.MaxValue)
                return Failure<ConversationInboxPage>(MessagingServiceStatus.ValidationFailed, "Conversations.InvalidPagination", "", ConversationInboxParameters.MAX_PAGE_SIZE);
            var query = ParticipatingConversations(database, actorId);
            var total = await query.CountAsync(token);
            var rows = await query.Select(conversation => new
            {
                conversation.ConversationId, conversation.ListingId, conversation.Listing.Title, conversation.Listing.Status,
                IsOwner = conversation.Listing.OwnerId == actorId, conversation.CreatedAtUtc,
                DisplayName = conversation.RenterId == actorId ? conversation.Listing.Owner.DisplayName : conversation.Renter.DisplayName,
                ImageId = conversation.Listing.Status == ListingStatus.Published || conversation.Listing.OwnerId == actorId
                    ? conversation.Listing.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId)
                        .Select(image => (int?)image.ListingImageId).FirstOrDefault() : null,
                ImageAlt = conversation.Listing.Status == ListingStatus.Published || conversation.Listing.OwnerId == actorId
                    ? conversation.Listing.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId)
                        .Select(image => image.AltText).FirstOrDefault() : null,
                Preview = conversation.Messages.OrderByDescending(message => message.MessageId)
                    .Select(message => message.Content.Length > PREVIEW_LENGTH ? message.Content.Substring(0, PREVIEW_LENGTH) : message.Content).FirstOrDefault(),
                Activity = conversation.Messages.OrderByDescending(message => message.MessageId).Select(message => (DateTime?)message.SentAtUtc)
                    .FirstOrDefault() ?? conversation.CreatedAtUtc,
                Unread = conversation.Messages.Count(message => message.SenderId != actorId && message.ReadAtUtc == null)
            }).OrderByDescending(row => row.Activity).ThenByDescending(row => row.ConversationId)
                .Skip((page.PageNumber - 1) * page.PageSize).Take(page.PageSize).ToListAsync(token);
            var items = rows.Select(row => new ConversationInboxItem(new(row.ConversationId, row.ListingId, row.Title, row.Status,
                row.IsOwner, row.DisplayName, row.ImageId.HasValue ? new(row.ImageId.Value, row.ImageAlt) : null, row.CreatedAtUtc), row.Preview, row.Activity, row.Unread)).ToArray();
            return MessagingResult<ConversationInboxPage>.Success(new(items, total, page.PageNumber, page.PageSize));
        }, cancellationToken);

    /// <summary>Counts only received unread messages after authorizing an optional conversation scope.</summary>
    public Task<MessagingResult<int>> GetUnreadCountAsync(ClaimsPrincipal? user, int? conversationId = null, CancellationToken cancellationToken = default) =>
        ReadAsync(user, async (database, actorId, token) =>
        {
            if (conversationId.HasValue && !await ParticipatingConversations(database, actorId)
                .AnyAsync(conversation => conversation.ConversationId == conversationId.Value, token)) return Unavailable<int>();
            var query = ReceivedUnread(database, actorId);
            if (conversationId.HasValue) query = query.Where(message => message.ConversationId == conversationId.Value);
            return MessagingResult<int>.Success(await query.CountAsync(token));
        }, cancellationToken);

    /// <summary>Projects retained context using public display information and the existing owner/public image visibility policy.</summary>
    private static IQueryable<ConversationDetails> ConversationProjection(IQueryable<Conversation> query, string actorId) => query.Select(conversation =>
        new ConversationDetails(conversation.ConversationId, conversation.ListingId, conversation.Listing.Title, conversation.Listing.Status,
            conversation.Listing.OwnerId == actorId,
            conversation.RenterId == actorId ? conversation.Listing.Owner.DisplayName : conversation.Renter.DisplayName,
            conversation.Listing.Status == ListingStatus.Published || conversation.Listing.OwnerId == actorId
                ? conversation.Listing.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId)
                    .Select(image => new ConversationImage(image.ListingImageId, image.AltText)).FirstOrDefault() : null, conversation.CreatedAtUtc));
}
