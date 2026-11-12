using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RentRoad.Data;
using RentRoad.Models;
using RentRoad.Models.Enums;
using RentRoad.Services.Listings;

namespace RentRoad.Services.Rentals;

public sealed partial class RentalRequestService
{
    /// <summary>Approves a pending request only for its listing owner, preserving dates and agreed financial snapshots.</summary>
    public Task<RentalRequestServiceResult> ApproveAsync(int requestId, ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        DecideAsync(requestId, user, RentalStatus.Approved, cancellationToken);

    /// <summary>Declines an owner's pending request while preserving its history and setting its decision timestamp once.</summary>
    public Task<RentalRequestServiceResult> DeclineAsync(int requestId, ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        DecideAsync(requestId, user, RentalStatus.Declined, cancellationToken);

    /// <summary>Cancels only a renter's pending request or an approved request before its first rental day begins.</summary>
    public Task<RentalRequestServiceResult> CancelAsync(int requestId, ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        DecideAsync(requestId, user, RentalStatus.Cancelled, cancellationToken);

    /// <summary>Serializes each authorized transition with availability edits, rereading committed state on every provider retry.</summary>
    private async Task<RentalRequestServiceResult> DecideAsync(int requestId, ClaimsPrincipal? user, RentalStatus target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actorId = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return Failure(RentalRequestServiceStatus.Unauthenticated, string.Empty, "RentalRequest.SignInToManage");
        }
        if (requestId <= 0)
        {
            return Failure(RentalRequestServiceStatus.ValidationFailed, nameof(RentalRequest.RentalRequestId), "RentalRequest.InvalidRequestId");
        }
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
        {
            return Failure(RentalRequestServiceStatus.Conflict, string.Empty, "RentalRequest.TransactionInProgress");
        }
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var database = CreateWriteContext();
                var listingId = await AuthorizedRequests(database, requestId, actorId, target)
                    .Select(request => (int?)request.ListingId).SingleOrDefaultAsync(token);
                if (!listingId.HasValue)
                {
                    return RequestUnavailable();
                }
                await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await ListingTransactionLock.AcquireAsync(database, listingId.Value, token);
                var request = await AuthorizedRequests(database, requestId, actorId, target)
                    .SingleOrDefaultAsync(request => request.ListingId == listingId.Value, token);
                if (request is null)
                {
                    return RequestUnavailable();
                }
                var result = await ApplyDecisionAsync(database, request, target, token);
                if (result.IsSuccess)
                {
                    await transaction.CommitAsync(token);
                }
                return result;
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or RetryLimitExceededException)
        {
            logger.LogError(exception, "Rental decision {Target} could not be confirmed for request {RequestId}.", target, requestId);
            return await VerifyDecisionAsync(requestId, actorId, target, exception, cancellationToken);
        }
    }

    /// <summary>Scopes both initial lookup and locked rereads to the authenticated actor without revealing another user's request data.</summary>
    private static IQueryable<RentalRequest> AuthorizedRequests(ApplicationDbContext database, int requestId, string actorId, RentalStatus target) =>
        database.RentalRequests.AsNoTracking().Where(request => request.RentalRequestId == requestId
            && (target == RentalStatus.Cancelled ? request.RenterId == actorId : request.Listing.OwnerId == actorId));

    /// <summary>Checks legal transitions and date/availability rules, then conditionally updates only status and the applicable timestamp.</summary>
    private async Task<RentalRequestServiceResult> ApplyDecisionAsync(ApplicationDbContext database, RentalRequest request,
        RentalStatus target, CancellationToken cancellationToken)
    {
        if (request.Status == target)
        {
            return RentalRequestServiceResult.Success(request, alreadyApplied: true);
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        if (target == RentalStatus.Cancelled)
        {
            if (request.Status != RentalStatus.Pending && request.Status != RentalStatus.Approved)
            {
                return Failure(RentalRequestServiceStatus.Conflict, nameof(request.Status), "RentalRequest.InvalidTransition");
            }
            if (request.Status == RentalStatus.Approved && request.StartDate <= today)
            {
                return Failure(RentalRequestServiceStatus.Conflict, nameof(request.StartDate), "RentalRequest.CancellationTooLate");
            }
        }
        else if (request.Status != (target switch
        {
            RentalStatus.Active => RentalStatus.Approved,
            RentalStatus.Completed => RentalStatus.Active,
            _ => RentalStatus.Pending
        }))
        {
            return Failure(RentalRequestServiceStatus.Conflict, nameof(request.Status), "RentalRequest.InvalidTransition");
        }
        var lifecycleError = ValidateLifecycleDates(request, target, today);
        if (lifecycleError is not null)
        {
            return RentalRequestServiceResult.Failure(RentalRequestServiceStatus.Conflict, lifecycleError);
        }
        if (target == RentalStatus.Approved)
        {
            var error = await ValidateApprovalAsync(database, request, today, cancellationToken);
            if (error is not null)
            {
                return RentalRequestServiceResult.Failure(RentalRequestServiceStatus.Conflict, error);
            }
        }
        var update = database.RentalRequests.Where(stored => stored.RentalRequestId == request.RentalRequestId
            && stored.ListingId == request.ListingId && stored.Status == request.Status);
        var changed = target switch
        {
            RentalStatus.Approved or RentalStatus.Declined => await update.ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.Status, target)
                .SetProperty(stored => stored.DecisionAtUtc, (DateTime?)now), cancellationToken),
            RentalStatus.Completed => await update.ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.Status, target)
                .SetProperty(stored => stored.CompletedAtUtc, (DateTime?)now), cancellationToken),
            _ => await update.ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.Status, target), cancellationToken)
        };
        if (changed != 1)
        {
            return Failure(RentalRequestServiceStatus.ConcurrencyFailed, nameof(request.Status), "RentalRequest.ConcurrentChange");
        }
        request.Status = target;
        if (target is RentalStatus.Approved or RentalStatus.Declined)
        {
            request.DecisionAtUtc = now;
        }
        else if (target == RentalStatus.Completed)
        {
            request.CompletedAtUtc = now;
        }
        return RentalRequestServiceResult.Success(request);
    }

    /// <summary>Rechecks published visibility and inclusive-start/exclusive-end coverage before creating a reservation.</summary>
    private static async Task<RentalRequestError?> ValidateApprovalAsync(ApplicationDbContext database, RentalRequest request,
        DateOnly today, CancellationToken cancellationToken)
    {
        if (request.StartDate < today)
        {
            return new(nameof(request.StartDate), "RentalRequest.ApprovalTooLate");
        }
        if (!await database.Listings.AsNoTracking().AnyAsync(listing => listing.ListingId == request.ListingId
            && listing.Status == ListingStatus.Published, cancellationToken))
        {
            return new(nameof(request.ListingId), "RentalRequest.ListingNotPublished");
        }
        return await ValidateAvailabilityAsync(database, new RentalRequestCreate(request.ListingId, request.StartDate, request.EndDate), cancellationToken);
    }

    /// <summary>Verifies a possibly committed target state using an authorized read, never a second blind write.</summary>
    private async Task<RentalRequestServiceResult> VerifyDecisionAsync(int requestId, string actorId, RentalStatus target,
        Exception failure, CancellationToken cancellationToken)
    {
        try
        {
            await using var database = CreateWriteContext();
            var stored = await AuthorizedRequests(database, requestId, actorId, target).SingleOrDefaultAsync(cancellationToken);
            if (stored?.Status == target)
            {
                return RentalRequestServiceResult.Success(stored, alreadyApplied: true);
            }
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or RetryLimitExceededException)
        {
            logger.LogWarning(exception, "Rental decision verification failed for request {RequestId}.", requestId);
        }
        var conflict = IsConcurrencyFailure(failure);
        return Failure(conflict ? RentalRequestServiceStatus.ConcurrencyFailed : RentalRequestServiceStatus.PersistenceFailed,
            nameof(RentalRequest.Status), conflict ? "RentalRequest.ConcurrentChange" : "RentalRequest.DecisionUnconfirmed");
    }

    /// <summary>Recognizes exhausted SQL concurrency failures even when a retry wrapper contains the provider exception.</summary>
    private static bool IsConcurrencyFailure(Exception failure)
    {
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException || current is SqlException sql
                && sql.Errors.Cast<SqlError>().Any(error => error.Number is 1205 or 1222 or 3960))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Uses the same opaque result for missing and unauthorized request identifiers.</summary>
    private static RentalRequestServiceResult RequestUnavailable() => Failure(RentalRequestServiceStatus.NotFound,
        nameof(RentalRequest.RentalRequestId), "RentalRequest.RequestUnavailable");
}
