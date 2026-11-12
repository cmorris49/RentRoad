using System.Security.Claims;
using RentRoad.Models;
using RentRoad.Models.Enums;

namespace RentRoad.Services.Rentals;

public sealed partial class RentalRequestService
{
    /// <summary>Lets the listing owner activate an approved rental once its first UTC calendar day has arrived, including overdue rentals.</summary>
    public Task<RentalRequestServiceResult> ActivateAsync(int requestId, ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        DecideAsync(requestId, user, RentalStatus.Active, cancellationToken);

    /// <summary>Lets the listing owner complete an active rental at or after its exclusive end date, preserving its original terms.</summary>
    public Task<RentalRequestServiceResult> CompleteAsync(int requestId, ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
        DecideAsync(requestId, user, RentalStatus.Completed, cancellationToken);

    /// <summary>Applies only calendar-date lower bounds so overdue approved or active rentals can still be processed manually.</summary>
    private static RentalRequestError? ValidateLifecycleDates(RentalRequest request, RentalStatus target, DateOnly today) => target switch
    {
        RentalStatus.Active when today < request.StartDate => new(nameof(request.StartDate), "RentalRequest.ActivationTooEarly"),
        RentalStatus.Completed when today < request.EndDate => new(nameof(request.EndDate), "RentalRequest.CompletionTooEarly"),
        _ => null
    };
}
