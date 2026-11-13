using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using RentRoad.Data;
using RentRoad.Models;
using RentRoad.Models.Enums;
using RentRoad.Services.Listings;

namespace RentRoad.Services.Rentals;

/// <summary>
/// Creates requests and applies authorized decisions while protecting availability and immutable rental terms.
/// </summary>
public sealed partial class RentalRequestService : IRentalRequestService
{
    private readonly ApplicationDbContext context;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<RentalRequestService> logger;

    /// <summary>
    /// Initializes rental operations with the existing relational context, UTC clock and diagnostic logger.
    /// </summary>
    /// <param name="context">The existing scoped database context.</param>
    /// <param name="timeProvider">The clock used to obtain UTC timestamps and calendar dates.</param>
    /// <param name="logger">The logger used to record operational failures.</param>
    public RentalRequestService(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<RentalRequestService> logger)
    {
        this.context = context;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Validates committed listing terms and inserts at most one identical pending submission within a serializable
    /// unit of work.
    /// </summary>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="details">The listing and half-open rental period submitted for the request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    public async Task<RentalRequestServiceResult> CreateAsync(
        ClaimsPrincipal? user,
        RentalRequestCreate? details,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var renterId = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(renterId))
        {
            return Failure(
                RentalRequestServiceStatus.Unauthenticated,
                nameof(RentalRequest.RenterId),
                "RentalRequest.SignInRequired");
        }

        if (details is null)
        {
            return Failure(
                RentalRequestServiceStatus.ValidationFailed,
                string.Empty,
                "RentalRequest.DetailsRequired");
        }

        var submittedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var errors = ValidateInput(details, DateOnly.FromDateTime(submittedAtUtc));

        if (errors.Count > 0)
        {
            return RentalRequestServiceResult.Failure(
                RentalRequestServiceStatus.ValidationFailed,
                errors.ToArray());
        }

        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
        {
            return Failure(
                RentalRequestServiceStatus.Conflict,
                string.Empty,
                "RentalRequest.TransactionInProgress");
        }

        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(
                async token =>
                {
                    await using var writeContext = CreateWriteContext();
                    await using var transaction = await writeContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        token);
                    var result = await CreatePendingAsync(writeContext, renterId, details, submittedAtUtc, token);

                    if (result.IsSuccess)
                    {
                        await transaction.CommitAsync(token);
                    }

                    return result;
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or RetryLimitExceededException)
        {
            logger.LogError(
                exception,
                "Rental request creation could not be confirmed for listing {ListingId}.",
                details.ListingId);
            return Failure(
                RentalRequestServiceStatus.PersistenceFailed,
                string.Empty,
                "RentalRequest.PersistenceUnconfirmed");
        }
    }

    /// <summary>
    /// Checks strictly future calendar dates without converting submitted dates into timestamps or adjusting time
    /// zones.
    /// </summary>
    /// <param name="details">The listing and half-open rental period submitted for the request.</param>
    /// <param name="today">The current UTC calendar date used for date validation.</param>
    /// <returns>The validation errors found by these checks.</returns>
    private static List<RentalRequestError> ValidateInput(RentalRequestCreate details, DateOnly today)
    {
        var errors = new List<RentalRequestError>();

        if (details.ListingId <= 0)
        {
            errors.Add(new(nameof(details.ListingId), "RentalRequest.InvalidListingId"));
        }

        if (details.StartDate == default)
        {
            errors.Add(new(nameof(details.StartDate), "RentalRequest.StartRequired"));
        }
        else if (details.StartDate <= today)
        {
            errors.Add(new(nameof(details.StartDate), "RentalRequest.FutureStartRequired"));
        }

        if (details.EndDate == default)
        {
            errors.Add(new(nameof(details.EndDate), "RentalRequest.EndRequired"));
        }
        else if (details.EndDate <= details.StartDate)
        {
            errors.Add(new(nameof(details.EndDate), "RentalRequest.PositiveDurationRequired"));
        }

        return errors;
    }

    /// <summary>
    /// Uses a fresh tracker per provider retry so scoped pending edits cannot leak into the request insertion.
    /// </summary>
    /// <returns>The independent database context using the existing provider and model.</returns>
    private ApplicationDbContext CreateWriteContext() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>((DbContextOptions<ApplicationDbContext>)context.GetService<IDbContextOptions>())
                .UseModel(context.Model).Options);

    /// <summary>
    /// Checks identity, visibility and stored duplicates before validating new terms and persisting their price
    /// snapshot.
    /// </summary>
    /// <param name="database">The database context used for this operation.</param>
    /// <param name="renterId">The renter's account identifier.</param>
    /// <param name="details">The listing and half-open rental period submitted for the request.</param>
    /// <param name="submittedAtUtc">The request submission timestamp in UTC.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    private async Task<RentalRequestServiceResult> CreatePendingAsync(
        ApplicationDbContext database,
        string renterId,
        RentalRequestCreate details,
        DateTime submittedAtUtc,
        CancellationToken cancellationToken)
    {
        var storedRenterId = await database.Users.AsNoTracking().Where(user => user.Id == renterId)
            .Select(user => user.Id).SingleOrDefaultAsync(cancellationToken);

        if (storedRenterId is null)
        {
            return Failure(
                RentalRequestServiceStatus.NotFound,
                nameof(RentalRequest.RenterId),
                "RentalRequest.AccountNotFound");
        }

        renterId = storedRenterId;
        await ListingTransactionLock.AcquireAsync(database, details.ListingId, cancellationToken);
        var listing = await database.Listings.AsNoTracking().Where(item => item.ListingId == details.ListingId)
            .Select(item => new
            {
                IsOwner = item.OwnerId == renterId,
                item.Status,
                item.DailyPriceCents
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (listing is null)
        {
            return Failure(
                RentalRequestServiceStatus.NotFound,
                nameof(details.ListingId),
                "RentalRequest.ListingNotFound");
        }

        if (listing.Status != ListingStatus.Published)
        {
            return Failure(
                RentalRequestServiceStatus.Conflict,
                nameof(details.ListingId),
                "RentalRequest.ListingNotPublished");
        }

        if (listing.IsOwner)
        {
            return Failure(
                RentalRequestServiceStatus.Forbidden,
                nameof(details.ListingId),
                "RentalRequest.SelfRentalForbidden");
        }

        var existing = await database.RentalRequests.AsNoTracking()
            .Where(request => request.ListingId == details.ListingId
                && request.RenterId == renterId
                && request.Status == RentalStatus.Pending
                && request.StartDate == details.StartDate
                && request.EndDate == details.EndDate
                && request.PickupTime == details.PickupTime
                && request.ReturnTime == details.ReturnTime)
            .OrderBy(request => request.RentalRequestId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return RentalRequestServiceResult.Success(existing, alreadyPending: true);
        }

        if (!listing.DailyPriceCents.HasValue || listing.DailyPriceCents.Value <= 0)
        {
            return Failure(
                RentalRequestServiceStatus.Conflict,
                nameof(Listing.DailyPriceCents),
                "RentalRequest.InvalidPrice");
        }

        long totalPrice;

        try
        {
            totalPrice = checked(listing.DailyPriceCents.Value * (details.EndDate.DayNumber - details.StartDate.DayNumber));
        }
        catch (OverflowException)
        {
            return Failure(
                RentalRequestServiceStatus.ValidationFailed,
                nameof(details.EndDate),
                "RentalRequest.PriceOverflow");
        }

        var availabilityError = await ValidateAvailabilityAsync(database, details, cancellationToken);

        if (availabilityError is not null)
        {
            return RentalRequestServiceResult.Failure(RentalRequestServiceStatus.Conflict, availabilityError);
        }

        var request = new RentalRequest
        {
            ListingId = details.ListingId,
            RenterId = renterId,
            StartDate = details.StartDate,
            EndDate = details.EndDate,
            PickupTime = details.PickupTime,
            ReturnTime = details.ReturnTime,
            DailyPriceAtRequestCents = listing.DailyPriceCents.Value,
            TotalPriceCents = totalPrice,
            Status = RentalStatus.Pending,
            CreatedAtUtc = submittedAtUtc
        };

        database.RentalRequests.Add(request);
        await database.SaveChangesAsync(cancellationToken);
        return RentalRequestServiceResult.Success(request);
    }

    /// <summary>
    /// Requires complete window coverage and excludes half-open intersections with blocks and approved/active
    /// reservations.
    /// </summary>
    /// <param name="database">The database context used for this operation.</param>
    /// <param name="details">The listing and half-open rental period submitted for the request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the first date or availability error, or null when these checks pass.</returns>
    private static async Task<RentalRequestError?> ValidateAvailabilityAsync(
        ApplicationDbContext database,
        RentalRequestCreate details,
        CancellationToken cancellationToken)
    {
        var windows = await database.AvailabilityWindows.AsNoTracking()
            .Where(window => window.ListingId == details.ListingId
                && window.StartDate < details.EndDate
                && details.StartDate < window.EndDate)
            .Select(window => new ListingAvailabilityPeriod(window.StartDate, window.EndDate))
            .ToListAsync(cancellationToken);

        if (!AvailabilityCoverage.CoversPeriod(
            windows,
            new ListingAvailabilityPeriod(details.StartDate, details.EndDate)))
        {
            return new(nameof(details.EndDate), "RentalRequest.OutsideAvailability");
        }

        if (await database.AvailabilityBlocks.AsNoTracking().AnyAsync(
            block => block.ListingId == details.ListingId
                && block.StartDate < details.EndDate && details.StartDate < block.EndDate,
            cancellationToken))
        {
            return new(nameof(details.EndDate), "RentalRequest.BlockedDates");
        }

        return await database.RentalRequests.AsNoTracking()
            .AnyAsync(
            request => request.ListingId == details.ListingId
                && (request.Status == RentalStatus.Approved || request.Status == RentalStatus.Active)
                && request.StartDate < details.EndDate
                && details.StartDate < request.EndDate,
            cancellationToken)
            ? new(nameof(details.EndDate), "RentalRequest.ReservedDates")
            : null;
    }

    /// <summary>
    /// Creates one actionable error with a stable resource key and unchanged field identifier.
    /// </summary>
    /// <param name="status">The operation or lifecycle status associated with the value.</param>
    /// <param name="field">The field associated with a validation error.</param>
    /// <param name="resourceKey">The shared resource key describing the error.</param>
    /// <returns>The operation result containing the returned value or structured errors.</returns>
    private static RentalRequestServiceResult Failure(
        RentalRequestServiceStatus status,
        string field,
        string resourceKey) =>
        RentalRequestServiceResult.Failure(status, new RentalRequestError(field, resourceKey));
}
