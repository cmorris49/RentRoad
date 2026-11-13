using System.Security.Claims;

namespace RentRoad.Services.Rentals;

/// <summary>
/// Creates rental requests and applies authorized owner decisions, lifecycle confirmations and renter cancellations.
/// </summary>
public interface IRentalRequestService
{
    /// <summary>
    /// Validates and submits a half-open rental period, returning an existing identical pending request on
    /// resubmission.
    /// </summary>
    /// <param name="user">The authenticated Identity principal; submitted renter identifiers are never accepted.</param>
    /// <param name="details">The listing, calendar dates and informational pickup/return times.</param>
    /// <param name="cancellationToken">Cancels database work without automatically repeating a submission.</param>
    /// <returns>The stored pending request or structured, resource-backed errors.</returns>
    Task<RentalRequestServiceResult> CreateAsync(
        ClaimsPrincipal? user,
        RentalRequestCreate? details,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves an owner's pending request after atomically rechecking listing availability and overlapping
    /// reservations.
    /// </summary>
    /// <param name="requestId">The identifier of the rental request being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<RentalRequestServiceResult> ApproveAsync(
        int requestId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Declines an owner's pending request without deleting its terms or affecting other pending requests.
    /// </summary>
    /// <param name="requestId">The identifier of the rental request being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<RentalRequestServiceResult> DeclineAsync(
        int requestId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the renter's pending request or an approved request whose first rental day is still in the future.
    /// </summary>
    /// <param name="requestId">The identifier of the rental request being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<RentalRequestServiceResult> CancelAsync(
        int requestId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates the owner's approved rental once its inclusive first UTC calendar day has arrived, including
    /// overdue handoffs.
    /// </summary>
    /// <param name="requestId">The identifier of the rental request being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<RentalRequestServiceResult> ActivateAsync(
        int requestId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the owner's active rental once its exclusive end date has arrived, setting its completion
    /// timestamp once.
    /// </summary>
    /// <param name="requestId">The identifier of the rental request being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<RentalRequestServiceResult> CompleteAsync(
        int requestId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);
}
