using System.Data.Common;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RentRoad.Data;
using RentRoad.Models.Enums;

namespace RentRoad.Services.Rentals;

/// <summary>Projects bounded rental histories without tracking entities or loading Identity and storage graphs.</summary>
public sealed class RentalManagementReadService : IRentalManagementReadService
{
    private readonly ApplicationDbContext context;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<RentalManagementReadService> logger;

    /// <summary>Initializes isolated read responsibilities using the existing context and UTC clock.</summary>
    public RentalManagementReadService(ApplicationDbContext context, TimeProvider timeProvider, ILogger<RentalManagementReadService> logger)
    {
        this.context = context;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Reads the authenticated renter's history, including closed requests and archived listing history.</summary>
    public Task<RentalHistoryResult> GetMineAsync(ClaimsPrincipal? user, RentalHistoryParameters parameters, CancellationToken cancellationToken = default) =>
        ReadAsync(user, parameters, false, cancellationToken);

    /// <summary>Reads incoming requests solely through the authenticated owner's listing relationship.</summary>
    public Task<RentalHistoryResult> GetIncomingAsync(ClaimsPrincipal? user, RentalHistoryParameters parameters, CancellationToken cancellationToken = default) =>
        ReadAsync(user, parameters, true, cancellationToken);

    /// <summary>Scopes both queries before counting and paging, then computes advisory actions from one UTC date snapshot.</summary>
    private async Task<RentalHistoryResult> ReadAsync(ClaimsPrincipal? user, RentalHistoryParameters parameters, bool incoming, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actorId = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(actorId)) return Failure(RentalRequestServiceStatus.Unauthenticated, "RentalRequest.SignInToManage");
        if (parameters.PageNumber < 1 || parameters.PageSize < 1 || parameters.PageSize > RentalHistoryParameters.MAX_PAGE_SIZE
            || ((long)parameters.PageNumber - 1) * parameters.PageSize > int.MaxValue)
            return Failure(RentalRequestServiceStatus.ValidationFailed, "RentalManagement.InvalidPagination");
        try
        {
            var query = context.RentalRequests.AsNoTracking().Where(request => incoming
                ? request.Listing.OwnerId == actorId : request.RenterId == actorId);
            var total = await query.CountAsync(cancellationToken);
            var rows = await query.OrderByDescending(request => request.CreatedAtUtc).ThenByDescending(request => request.RentalRequestId)
                .Skip((parameters.PageNumber - 1) * parameters.PageSize).Take(parameters.PageSize)
                .Select(request => new
                {
                    request.RentalRequestId, request.ListingId, request.Listing.Title,
                    ImageId = request.Listing.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId)
                        .Select(image => (int?)image.ListingImageId).FirstOrDefault(),
                    ImageAlt = request.Listing.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId)
                        .Select(image => image.AltText).FirstOrDefault(),
                    DisplayName = incoming ? request.Renter.DisplayName : request.Listing.Owner.DisplayName,
                    ListingStatus = request.Listing.Status, request.DailyPriceAtRequestCents, request.TotalPriceCents,
                    request.StartDate, request.EndDate, request.PickupTime, request.ReturnTime, request.Status,
                    request.CreatedAtUtc, request.DecisionAtUtc, request.CompletedAtUtc
                }).ToListAsync(cancellationToken);
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            var items = rows.Select(row => new RentalHistoryItem(row.RentalRequestId, row.ListingId, row.Title,
                row.ImageId.HasValue && (incoming || row.ListingStatus == ListingStatus.Published) ? new RentalHistoryImage(row.ImageId.Value, row.ImageAlt) : null, row.DisplayName,
                row.DailyPriceAtRequestCents, row.TotalPriceCents, row.StartDate, row.EndDate, row.PickupTime, row.ReturnTime,
                row.Status, row.CreatedAtUtc, row.DecisionAtUtc, row.CompletedAtUtc,
                new RentalAvailableActions(!incoming && (row.Status == RentalStatus.Pending || row.Status == RentalStatus.Approved && today < row.StartDate),
                    incoming && row.Status == RentalStatus.Pending && row.ListingStatus == ListingStatus.Published && today <= row.StartDate,
                    incoming && row.Status == RentalStatus.Pending,
                    incoming && row.Status == RentalStatus.Approved && today >= row.StartDate,
                    incoming && row.Status == RentalStatus.Active && today >= row.EndDate))
                { IsListingPublic = row.ListingStatus == ListingStatus.Published }).ToArray();
            return new(RentalRequestServiceStatus.Success, new(items, total, parameters.PageNumber, parameters.PageSize), Array.Empty<RentalRequestError>());
        }
        catch (Exception exception) when (exception is DbException or RetryLimitExceededException)
        {
            logger.LogError(exception, "Rental history retrieval failed.");
            return Failure(RentalRequestServiceStatus.PersistenceFailed, "RentalManagement.ReadUnavailable");
        }
    }

    /// <summary>Returns safe feedback without any participant or rental data.</summary>
    private static RentalHistoryResult Failure(RentalRequestServiceStatus status, string resourceKey) =>
        new(status, null, new[] { new RentalRequestError(string.Empty, resourceKey) });
}
