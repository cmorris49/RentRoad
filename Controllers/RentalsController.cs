using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Services.Rentals;
using RentRoad.ViewModels.Rentals;

namespace RentRoad.Controllers;

/// <summary>Adapts scoped rental histories and authoritative lifecycle operations to authenticated MVC.</summary>
[Authorize]
[Route("rentals")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RentalsController : Controller
{
    private readonly IRentalManagementReadService history;
    private readonly IRentalRequestService rentals;

    /// <summary>Initializes participant-scoped reads and existing transactional lifecycle operations.</summary>
    public RentalsController(IRentalManagementReadService history, IRentalRequestService rentals)
    {
        this.history = history;
        this.rentals = rentals;
    }

    /// <summary>Displays My Rentals using only the current renter's paginated history.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> MineAsync([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = RentalHistoryParameters.DEFAULT_PAGE_SIZE,
        CancellationToken cancellationToken = default) => !ModelState.IsValid ? InvalidPagination() :
        HistoryView(await history.GetMineAsync(User, new(pageNumber, pageSize), cancellationToken), false);

    /// <summary>Displays Incoming Requests using only requests for the current owner's listings.</summary>
    [HttpGet("incoming")]
    public async Task<IActionResult> IncomingAsync([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = RentalHistoryParameters.DEFAULT_PAGE_SIZE,
        CancellationToken cancellationToken = default) => !ModelState.IsValid ? InvalidPagination() :
        HistoryView(await history.GetIncomingAsync(User, new(pageNumber, pageSize), cancellationToken), true);

    /// <summary>Delegates owner approval, including transactional availability revalidation, to the existing service.</summary>
    [HttpPost("{id:int}/approve")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ApproveAsync([FromRoute] int id, CancellationToken cancellationToken = default,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = RentalHistoryParameters.DEFAULT_PAGE_SIZE) =>
        TransitionAsync(id, rentals.ApproveAsync, "Approve", true, pageNumber, pageSize, cancellationToken);

    /// <summary>Delegates owner decline without changing other pending requests.</summary>
    [HttpPost("{id:int}/decline")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DeclineAsync([FromRoute] int id, CancellationToken cancellationToken = default,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = RentalHistoryParameters.DEFAULT_PAGE_SIZE) =>
        TransitionAsync(id, rentals.DeclineAsync, "Decline", true, pageNumber, pageSize, cancellationToken);

    /// <summary>Delegates renter cancellation and its UTC-date cutoff to the existing service.</summary>
    [HttpPost("{id:int}/cancel")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CancelAsync([FromRoute] int id, CancellationToken cancellationToken = default,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = RentalHistoryParameters.DEFAULT_PAGE_SIZE) =>
        TransitionAsync(id, rentals.CancelAsync, "Cancel", false, pageNumber, pageSize, cancellationToken);

    /// <summary>Delegates owner activation, preserving overdue handling and the existing listing lock.</summary>
    [HttpPost("{id:int}/activate")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ActivateAsync([FromRoute] int id, CancellationToken cancellationToken = default,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = RentalHistoryParameters.DEFAULT_PAGE_SIZE) =>
        TransitionAsync(id, rentals.ActivateAsync, "Activate", true, pageNumber, pageSize, cancellationToken);

    /// <summary>Delegates owner completion without altering stored rental terms.</summary>
    [HttpPost("{id:int}/complete")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CompleteAsync([FromRoute] int id, CancellationToken cancellationToken = default,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = RentalHistoryParameters.DEFAULT_PAGE_SIZE) =>
        TransitionAsync(id, rentals.CompleteAsync, "Complete", true, pageNumber, pageSize, cancellationToken);

    /// <summary>Delegates route identifiers and the principal, then redirects with one-time feedback and bounded page context.</summary>
    private async Task<IActionResult> TransitionAsync(int id,
        Func<int, System.Security.Claims.ClaimsPrincipal?, CancellationToken, Task<RentalRequestServiceResult>> operation,
        string action, bool incoming, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || id <= 0)
            return Failure(RentalRequestServiceStatus.ValidationFailed, new[] { new RentalRequestError(string.Empty, "RentalRequest.InvalidRequestId") });
        TempData.Remove("RentalManagementFeedback");
        TempData.Remove("RentalManagementError");
        var result = await operation(id, User, cancellationToken);
        if (result.Status is RentalRequestServiceStatus.Unauthenticated or RentalRequestServiceStatus.Forbidden or RentalRequestServiceStatus.NotFound)
            return Failure(result.Status, result.Errors);
        if (result.IsSuccess)
            TempData["RentalManagementFeedback"] = SharedResource.Get($"RentalManagement.{action}{(result.AlreadyApplied ? "Repeated" : "Succeeded")}");
        else
            TempData["RentalManagementError"] = result.Errors.Count == 0 ? SharedResource.Get("RentalRequest.DecisionUnconfirmed")
                : string.Join(" ", result.Errors.Select(error => error.Message));
        var validContext = pageNumber > 0 && pageSize > 0 && pageSize <= RentalHistoryParameters.MAX_PAGE_SIZE
            && ((long)pageNumber - 1) * pageSize <= int.MaxValue;
        return RedirectToAction(incoming ? "Incoming" : "Mine", new
        {
            pageNumber = validContext ? pageNumber : 1,
            pageSize = validContext ? pageSize : RentalHistoryParameters.DEFAULT_PAGE_SIZE
        });
    }

    /// <summary>Renders scoped histories or safe read failures without mistaking dependency errors for an empty result.</summary>
    private IActionResult HistoryView(RentalHistoryResult result, bool incoming)
    {
        if (result.Status is RentalRequestServiceStatus.Unauthenticated or RentalRequestServiceStatus.Forbidden or RentalRequestServiceStatus.NotFound)
            return Failure(result.Status, result.Errors);
        var model = new RentalManagementViewModel(result.Value, incoming, result.Errors.Select(error => error.Message).ToArray());
        ViewData["Title"] = model.Heading;
        var view = View(incoming ? "Incoming" : "Mine", model);
        if (!result.IsSuccess) view.StatusCode = result.Status == RentalRequestServiceStatus.ValidationFailed
            ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable;
        return view;
    }

    /// <summary>Returns localized pagination feedback without accepting malformed query values as defaults.</summary>
    private IActionResult InvalidPagination() => Failure(RentalRequestServiceStatus.ValidationFailed,
        new[] { new RentalRequestError(string.Empty, "RentalManagement.InvalidPagination") });

    /// <summary>Preserves opaque missing/unauthorized results and safe HTTP feedback without exposing entities or diagnostics.</summary>
    private IActionResult Failure(RentalRequestServiceStatus status, IReadOnlyList<RentalRequestError> errors)
    {
        if (status == RentalRequestServiceStatus.Unauthenticated) return Challenge();
        if (status == RentalRequestServiceStatus.Forbidden) return Forbid();
        if (status == RentalRequestServiceStatus.NotFound) return NotFound();
        var statusCode = status switch
        {
            RentalRequestServiceStatus.ValidationFailed => StatusCodes.Status400BadRequest,
            RentalRequestServiceStatus.Conflict or RentalRequestServiceStatus.ConcurrencyFailed => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        return Problem(statusCode: statusCode, title: SharedResource.Get("Common.ActionFailed"),
            detail: errors.Count == 0 ? SharedResource.Get("RentalManagement.ReadUnavailable") : string.Join(" ", errors.Select(error => error.Message)));
    }
}
