using System.Security.Claims;

namespace RentRoad.Services.Rentals;

/// <summary>
/// Reads paginated participant-scoped histories independently of lifecycle writes.
/// </summary>
public interface IRentalManagementReadService
{
    /// <summary>
    /// Retrieves only requests submitted by the current authenticated renter.
    /// </summary>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="parameters">The requested one-based pagination settings.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<RentalHistoryResult> GetMineAsync(
        ClaimsPrincipal? user,
        RentalHistoryParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves only requests for listings belonging to the current authenticated owner.
    /// </summary>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="parameters">The requested one-based pagination settings.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<RentalHistoryResult> GetIncomingAsync(
        ClaimsPrincipal? user,
        RentalHistoryParameters parameters,
        CancellationToken cancellationToken = default);
}
