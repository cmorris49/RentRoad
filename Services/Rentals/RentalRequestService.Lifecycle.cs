using System.Security.Claims;
using RentRoad.Models;
using RentRoad.Models.Enums;

namespace RentRoad.Services.Rentals;

/// <summary>
/// Applies owner lifecycle confirmations using UTC calendar-date bounds.
/// </summary>
public sealed partial class RentalRequestService
{
    /// <summary>
    /// Lets the listing owner activate an approved rental once its first UTC calendar day has arrived, including
    /// overdue rentals.
    /// </summary>
    /// <param name="requestId">The identifier of the rental request being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    public Task<RentalRequestServiceResult> ActivateAsync(
        int requestId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default) =>
        DecideAsync(requestId, user, RentalStatus.Active, cancellationToken);

    /// <summary>
    /// Lets the listing owner complete an active rental at or after its exclusive end date, preserving its original
    /// terms.
    /// </summary>
    /// <param name="requestId">The identifier of the rental request being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    public Task<RentalRequestServiceResult> CompleteAsync(
        int requestId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default) =>
        DecideAsync(requestId, user, RentalStatus.Completed, cancellationToken);

    /// <summary>
    /// Applies only calendar-date lower bounds so overdue approved or active rentals can still be processed manually.
    /// </summary>
    /// <param name="request">The stored rental request whose terms and status are checked.</param>
    /// <param name="target">The requested rental lifecycle status.</param>
    /// <param name="today">The current UTC calendar date used for date validation.</param>
    /// <returns>The first date or availability error, or null when these checks pass.</returns>
    private static RentalRequestError? ValidateLifecycleDates(
        RentalRequest request,
        RentalStatus target,
        DateOnly today) =>
        target switch
        {
            RentalStatus.Active when today < request.StartDate => new(
                nameof(request.StartDate),
                "RentalRequest.ActivationTooEarly"),
            RentalStatus.Completed when today < request.EndDate => new(
                nameof(request.EndDate),
                "RentalRequest.CompletionTooEarly"),
            _ => null
        };
}
