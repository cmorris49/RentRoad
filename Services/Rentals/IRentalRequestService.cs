using System.Security.Claims;

namespace RentRoad.Services.Rentals;

/// <summary>Creates rental requests and applies authorized owner decisions, lifecycle confirmations and renter cancellations.</summary>
public interface IRentalRequestService
{
    /// <summary>Validates and submits a half-open rental period, returning an existing identical pending request on resubmission.</summary>
    /// <param name="user">The authenticated Identity principal; submitted renter identifiers are never accepted.</param>
    /// <param name="details">The listing, calendar dates and informational pickup/return times.</param>
    /// <param name="cancellationToken">Cancels database work without automatically repeating a submission.</param>
    /// <returns>The stored pending request or structured, resource-backed errors.</returns>
    Task<RentalRequestServiceResult> CreateAsync(ClaimsPrincipal? user, RentalRequestCreate? details,
        CancellationToken cancellationToken = default);

    /// <summary>Approves an owner's pending request after atomically rechecking listing availability and overlapping reservations.</summary>
    Task<RentalRequestServiceResult> ApproveAsync(int requestId, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>Declines an owner's pending request without deleting its terms or affecting other pending requests.</summary>
    Task<RentalRequestServiceResult> DeclineAsync(int requestId, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>Cancels the renter's pending request or an approved request whose first rental day is still in the future.</summary>
    Task<RentalRequestServiceResult> CancelAsync(int requestId, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>Activates the owner's approved rental once its inclusive first UTC calendar day has arrived, including overdue handoffs.</summary>
    Task<RentalRequestServiceResult> ActivateAsync(int requestId, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>Completes the owner's active rental once its exclusive end date has arrived, setting its completion timestamp once.</summary>
    Task<RentalRequestServiceResult> CompleteAsync(int requestId, ClaimsPrincipal? user, CancellationToken cancellationToken = default);
}
