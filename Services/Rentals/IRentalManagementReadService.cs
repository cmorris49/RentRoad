using System.Security.Claims;

namespace RentRoad.Services.Rentals;

/// <summary>Reads paginated participant-scoped histories independently of lifecycle writes.</summary>
public interface IRentalManagementReadService
{
    /// <summary>Retrieves only requests submitted by the current authenticated renter.</summary>
    Task<RentalHistoryResult> GetMineAsync(ClaimsPrincipal? user, RentalHistoryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>Retrieves only requests for listings belonging to the current authenticated owner.</summary>
    Task<RentalHistoryResult> GetIncomingAsync(ClaimsPrincipal? user, RentalHistoryParameters parameters, CancellationToken cancellationToken = default);
}
