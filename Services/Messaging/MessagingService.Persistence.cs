using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using RentRoad.Data;
using RentRoad.Models;

namespace RentRoad.Services.Messaging;

/// <summary>
/// Provides authenticated messaging units of work, locking and persistence failure handling.
/// </summary>
public sealed partial class MessagingService
{
    /// <summary>
    /// Authenticates reads using the existing Identity claim and canonical stored user key, mapping dependency
    /// failures safely.
    /// </summary>
    /// <typeparam name="T">The operation's result value type.</typeparam>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="operation">The asynchronous operation to execute within the surrounding service workflow.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    private async Task<MessagingResult<T>> ReadAsync<T>(
        ClaimsPrincipal? user,
        Func<ApplicationDbContext, string, CancellationToken, Task<MessagingResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var claimedId = AuthenticatedId(user);

        if (claimedId is null)
        {
            return Failure<T>(MessagingServiceStatus.Unauthenticated, "Messages.SignInRequired");
        }

        try
        {
            var actorId = await StoredActorAsync(context, claimedId, cancellationToken);

            return actorId is null
                ? Failure<T>(MessagingServiceStatus.Unauthenticated, "Messages.SignInRequired")
                : await operation(context, actorId, cancellationToken);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            LogFailure("Read", exception);
            return Failure<T>(MessagingServiceStatus.PersistenceFailed, "Messages.ReadUnavailable");
        }
    }

    /// <summary>
    /// Runs a fresh serializable unit per provider retry and verifies only known duplicate or potentially committed
    /// outcomes.
    /// </summary>
    /// <typeparam name="T">The operation's result value type.</typeparam>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="operation">The asynchronous operation to execute within the surrounding service workflow.</param>
    /// <param name="verify">The read operation used to verify a duplicate or potentially committed outcome.</param>
    /// <param name="uniqueIndex">The optional unique index recognized during duplicate-write recovery.</param>
    /// <param name="operationName">The operation name used for failure diagnostics.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    private async Task<MessagingResult<T>> WriteAsync<T>(
        ClaimsPrincipal? user,
        Func<ApplicationDbContext, string, CancellationToken, Task<MessagingResult<T>>> operation,
        Func<ApplicationDbContext, string, CancellationToken, Task<MessagingResult<T>?>> verify,
        string? uniqueIndex,
        string operationName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var claimedId = AuthenticatedId(user);

        if (claimedId is null)
        {
            return Failure<T>(MessagingServiceStatus.Unauthenticated, "Messages.SignInRequired");
        }

        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
        {
            return Failure<T>(MessagingServiceStatus.Conflict, "Messages.TransactionInProgress");
        }

        var commitAttempted = false;

        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(
                async token =>
                {
                    await using var database = FreshContext();
                    var actorId = await StoredActorAsync(database, claimedId, token);

                    if (actorId is null)
                    {
                        return Failure<T>(MessagingServiceStatus.Unauthenticated, "Messages.SignInRequired");
                    }

                    await using var transaction = await database.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        token);
                    var result = await operation(database, actorId, token);

                    if (result.IsSuccess)
                    {
                        commitAttempted = true;
                        await transaction.CommitAsync(token);
                    }

                    return result;
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            LogFailure(operationName, exception);

            if (commitAttempted || uniqueIndex is not null && IsExpectedUniqueViolation(exception, uniqueIndex))
            {
                try
                {
                    await using var database = FreshContext();
                    var actorId = await StoredActorAsync(database, claimedId, cancellationToken);

                    if (actorId is not null && await verify(database, actorId, cancellationToken) is { } confirmed)
                    {
                        return confirmed;
                    }
                }
                catch (Exception verificationFailure) when (IsDatabaseFailure(verificationFailure))
                {
                    LogFailure("Verify" + operationName, verificationFailure);
                }
            }

            return Failure<T>(
                IsConcurrencyFailure(exception)
                    ? MessagingServiceStatus.ConcurrencyFailed
                    : MessagingServiceStatus.PersistenceFailed,
                IsConcurrencyFailure(exception) ? "Messages.ConcurrentChange" : "Messages.PersistenceUnconfirmed");
        }
    }

    /// <summary>
    /// Copies provider/model/interceptor options but never the caller's tracked pending entities or current
    /// transaction.
    /// </summary>
    /// <returns>The independent database context using the existing provider and model.</returns>
    private ApplicationDbContext FreshContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>(
                (DbContextOptions<ApplicationDbContext>)context.GetService<IDbContextOptions>()).UseModel(context.Model).Options);

    /// <summary>
    /// Uses only a name-identifier claim belonging to an authenticated Identity identity.
    /// </summary>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <returns>The authenticated account identifier, or null when none is available.</returns>
    private static string? AuthenticatedId(ClaimsPrincipal? user)
    {
        var id = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>
    /// Resolves the real retained account key without loading private Identity properties or trusting a submitted
    /// identifier.
    /// </summary>
    /// <param name="database">The database context used for this operation.</param>
    /// <param name="claimedId">The authenticated account identifier read from the Identity principal.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the stored account identifier, or null when the claimed account does not exist.</returns>
    private static Task<string?> StoredActorAsync(
        ApplicationDbContext database,
        string claimedId,
        CancellationToken cancellationToken) =>
        database.Users.AsNoTracking()
            .Where(user => user.Id == claimedId)
            .Select(user => user.Id)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Scopes conversations to their renter or current immutable listing owner before any projection or aggregate.
    /// </summary>
    /// <param name="database">The database context used for this operation.</param>
    /// <param name="actorId">The authenticated actor's account identifier.</param>
    /// <returns>The query describing the selected scope or projection.</returns>
    private static IQueryable<Conversation> ParticipatingConversations(
        ApplicationDbContext database,
        string actorId) =>
        database.Conversations.AsNoTracking()
            .Where(conversation => conversation.RenterId == actorId || conversation.Listing.OwnerId == actorId);

    /// <summary>
    /// Scopes received unread messages to authorized conversations without counting the actor's outgoing messages.
    /// </summary>
    /// <param name="database">The database context used for this operation.</param>
    /// <param name="actorId">The authenticated actor's account identifier.</param>
    /// <returns>The query describing the selected scope or projection.</returns>
    private static IQueryable<Message> ReceivedUnread(ApplicationDbContext database, string actorId) =>
        database.Messages.AsNoTracking()
            .Where(message => message.ReadAtUtc == null && message.SenderId != actorId
                && (message.Conversation.RenterId == actorId || message.Conversation.Listing.OwnerId == actorId));

    /// <summary>
    /// Projects public sender display and exact text while keeping the other participant's submission token private.
    /// </summary>
    /// <param name="actorId">The authenticated actor's account identifier.</param>
    /// <returns>The expression projecting safe message data for the actor.</returns>
    private static Expression<Func<Message, MessageItem>> MessageProjection(string actorId) =>
        message => new MessageItem(
            message.MessageId,
            message.ConversationId,
            message.Content,
            message.Sender.DisplayName,
            message.SenderId == actorId,
            message.SentAtUtc,
            message.ReadAtUtc,
            message.SenderId == actorId ? message.ClientMessageId : null);

    /// <summary>
    /// Looks up only this authenticated sender's exact retry scope, independently of current listing visibility.
    /// </summary>
    /// <param name="database">The database context used for this operation.</param>
    /// <param name="conversationId">The conversation identifier, when supplied.</param>
    /// <param name="actorId">The authenticated actor's account identifier.</param>
    /// <param name="clientMessageId">The stable identifier used to recognize retries of the same message submission.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the saved message matching this sender and retry token, or null when absent.</returns>
    private static Task<MessageItem?> FindSubmissionAsync(
        ApplicationDbContext database,
        int conversationId,
        string actorId,
        Guid clientMessageId,
        CancellationToken cancellationToken) =>
        database.Messages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId
                && message.SenderId == actorId
                && message.ClientMessageId == clientMessageId
                && (message.Conversation.RenterId == actorId || message.Conversation.Listing.OwnerId == actorId))
            .Select(MessageProjection(actorId))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Serializes one existing authorized conversation through transaction completion, including message identity
    /// allocation.
    /// </summary>
    /// <param name="database">The database context used for this operation.</param>
    /// <param name="conversationId">The conversation identifier, when supplied.</param>
    /// <param name="actorId">The authenticated actor's account identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning whether the selected conversation exists for the actor after lock acquisition.</returns>
    private static async Task<bool> LockConversationAsync(
        ApplicationDbContext database,
        int conversationId,
        string actorId,
        CancellationToken cancellationToken)
    {
        if (conversationId <= 0)
        {
            return false;
        }

        return database.Database.IsSqlServer()
            ? (await ConversationLockQuery(database, conversationId, actorId)
                .ToListAsync(cancellationToken)).Count == 1
            : await ParticipatingConversations(database, actorId)
                .AnyAsync(
                conversation => conversation.ConversationId == conversationId,
                cancellationToken);
    }

    /// <summary>
    /// Builds a parameterized participant-scoped SQL Server conversation-row lock without accepting a client group
    /// or owner identifier.
    /// </summary>
    /// <param name="database">The database context used for this operation.</param>
    /// <param name="conversationId">The conversation identifier, when supplied.</param>
    /// <param name="actorId">The authenticated actor's account identifier.</param>
    /// <returns>The query describing the selected scope or projection.</returns>
    private static IQueryable<int> ConversationLockQuery(
        ApplicationDbContext database,
        int conversationId,
        string actorId) =>
        database.Database.SqlQuery<int>(
            $"SELECT [c].[ConversationId] AS [Value] FROM [Conversations] AS [c] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) INNER JOIN [Listings] AS [l] ON [c].[ListingId] = [l].[ListingId] WHERE [c].[ConversationId] = {conversationId} AND ([c].[RenterId] = {actorId} OR [l].[OwnerId] = {actorId})");

    /// <summary>
    /// Protects creation of a listing/renter pair through its existing unique key, without taking a global
    /// application lock.
    /// </summary>
    /// <param name="database">The database context used for this operation.</param>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="actorId">The authenticated actor's account identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task representing completion of lock acquisition within the current transaction.</returns>
    private static async Task LockPairAsync(
        ApplicationDbContext database,
        int listingId,
        string actorId,
        CancellationToken cancellationToken)
    {
        if (database.Database.IsSqlServer())
        {
            await PairLockQuery(database, listingId, actorId)
                .ToListAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Builds the parameterized SQL Server existing-or-missing pair lock used before conversation insertion.
    /// </summary>
    /// <param name="database">The database context used for this operation.</param>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="actorId">The authenticated actor's account identifier.</param>
    /// <returns>The query describing the selected scope or projection.</returns>
    private static IQueryable<int> PairLockQuery(ApplicationDbContext database, int listingId, string actorId) =>
        database.Database.SqlQuery<int>(
            $"SELECT [ConversationId] AS [Value] FROM [Conversations] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [ListingId] = {listingId} AND [RenterId] = {actorId}");

    /// <summary>
    /// Recognizes only the intended unique key, never an unrelated primary-key, validation or connectivity failure.
    /// </summary>
    /// <param name="exception">The exception being classified or logged.</param>
    /// <param name="index">The expected database unique-index name.</param>
    /// <returns>Whether the exception identifies the expected unique constraint.</returns>
    private static bool IsExpectedUniqueViolation(Exception exception, string index)
    {
        var sqliteColumns = index == MESSAGE_UNIQUE_INDEX
            ? "Messages.ConversationId, Messages.SenderId, Messages.ClientMessageId"
            : "Conversations.ListingId, Conversations.RenterId";

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql
                && sql.Errors.Cast<SqlError>()
                    .Any(error => (error.Number is 2601 or 2627)
                    && error.Message.Contains(index, StringComparison.Ordinal)))
            {
                return true;
            }

            if (current is DbException && current.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
                && current.Message.Contains(
                    "UNIQUE constraint failed: " + sqliteColumns,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recognizes exhausted concurrency failures without adding a dependency on the test-only SQLite provider.
    /// </summary>
    /// <param name="exception">The exception being classified or logged.</param>
    /// <returns>Whether the exception or an inner exception is a recognized concurrency failure.</returns>
    private static bool IsConcurrencyFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException
                || current is SqlException sql
                && sql.Errors.Cast<SqlError>().Any(error => error.Number is 1205 or 1222 or 3960))
            {
                return true;
            }

            if (current is DbException && current.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
                && (current.Message.Contains("database is locked", StringComparison.Ordinal)
                || current.Message.Contains("database table is locked", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Restricts database recovery to provider/update/retry exceptions while allowing cancellation and programming
    /// errors to propagate.
    /// </summary>
    /// <param name="exception">The exception being classified or logged.</param>
    /// <returns>Whether the exception is a recognized database or retry failure.</returns>
    private static bool IsDatabaseFailure(Exception exception) =>
        exception is DbException or DbUpdateException or RetryLimitExceededException or TimeoutException;

    /// <summary>
    /// Logs an operation, failure type and numeric provider code without SQL text, parameters, tokens or message
    /// bodies.
    /// </summary>
    /// <param name="operation">The diagnostic name of the operation.</param>
    /// <param name="exception">The exception being classified or logged.</param>
    private void LogFailure(string operation, Exception exception)
    {
        var cause = exception.GetBaseException();
        var code = cause is SqlException sql ? sql.Number : cause is DbException database ? database.ErrorCode : (int?)null;

        logger.LogWarning(
            "Messaging operation {Operation} could not be confirmed ({FailureType}, provider code {ProviderCode}).",
            operation,
            exception.GetType().Name,
            code);
    }

    /// <summary>
    /// Uses one opaque result for missing and unauthorized private conversation identifiers.
    /// </summary>
    /// <typeparam name="T">The operation's result value type.</typeparam>
    /// <returns>The operation result containing the returned value or structured errors.</returns>
    private static MessagingResult<T> Unavailable<T>() => Failure<T>(
            MessagingServiceStatus.NotFound,
            "Conversations.NotAvailable");

    /// <summary>
    /// Returns a structured resource-backed error without resolving localized text inside persistence.
    /// </summary>
    /// <typeparam name="T">The operation's result value type.</typeparam>
    /// <param name="status">The operation or lifecycle status associated with the value.</param>
    /// <param name="resourceKey">The shared resource key describing the error.</param>
    /// <param name="field">The field associated with a validation error.</param>
    /// <param name="arguments">The formatting arguments associated with the resource key.</param>
    /// <returns>The operation result containing the returned value or structured errors.</returns>
    private static MessagingResult<T> Failure<T>(
        MessagingServiceStatus status,
        string resourceKey,
        string field = "",
        params object[] arguments) =>
        MessagingResult<T>.Failure(status, new MessagingError(field, resourceKey, arguments));
}
