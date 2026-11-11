using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Services.Listings;
using RentRoad.ViewModels.Listings;

namespace RentRoad.Controllers;

/// <summary>
/// Adapts owner availability management to MVC while delegating date, rental, and publication rules to the listing service.
/// </summary>
[Authorize]
[Route("my/listings/{listingId:int}/availability")]
public sealed class ListingAvailabilityController : Controller
{
    private readonly IListingService listings;

    /// <summary>
    /// Initializes availability MVC with the existing listing service.
    /// </summary>
    public ListingAvailabilityController(IListingService listings)
    {
        this.listings = listings;
    }

    /// <summary>
    /// Retrieves the owned listing's windows, private blocks, and trusted lifecycle status.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> IndexAsync([FromRoute] int listingId, CancellationToken cancellationToken = default)
    {
        var result = await listings.GetOwnerAvailabilityAsync(listingId, User, cancellationToken);
        return result.IsSuccess ? View("Index", new ListingAvailabilityManagementViewModel(result.Value!)) : ReadFailure(result.Status);
    }

    /// <summary>
    /// Creates a window using the existing owner's date and reservation checks.
    /// </summary>
    [HttpPost("windows")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CreateWindowAsync([FromRoute] int listingId, [FromForm(Name = "")] ListingAvailabilityWindowInputViewModel input,
        CancellationToken cancellationToken = default) => ExecuteAsync(listingId, ListingAvailabilityOperation.CreateWindow, null, input, null,
            () => listings.CreateAvailabilityWindowAsync(listingId, User, input.ToServicePeriod(), cancellationToken), cancellationToken);

    /// <summary>
    /// Updates only the route-selected window's dates through the existing service.
    /// </summary>
    [HttpPost("windows/{windowId:int}/edit")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateWindowAsync([FromRoute] int listingId, [FromRoute] int windowId,
        [FromForm(Name = "")] ListingAvailabilityWindowInputViewModel input, CancellationToken cancellationToken = default) =>
        ExecuteAsync(listingId, ListingAvailabilityOperation.UpdateWindow, windowId, input, null,
            () => listings.UpdateAvailabilityWindowAsync(listingId, windowId, User, input.ToServicePeriod(), cancellationToken), cancellationToken);

    /// <summary>
    /// Removes the selected owned window only when the service permits the resulting configuration.
    /// </summary>
    [HttpPost("windows/{windowId:int}/remove")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RemoveWindowAsync([FromRoute] int listingId, [FromRoute] int windowId,
        CancellationToken cancellationToken = default) => ExecuteAsync(listingId, ListingAvailabilityOperation.RemoveWindow, windowId, null, null,
            () => listings.RemoveAvailabilityWindowAsync(listingId, windowId, User, cancellationToken), cancellationToken);

    /// <summary>
    /// Replaces all windows without binding or copying client-supplied listing scalar fields.
    /// </summary>
    [HttpPost("windows/replace")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ReplaceWindowsAsync([FromRoute] int listingId, [FromForm(Name = "")] ListingAvailabilityReplacementViewModel input,
        CancellationToken cancellationToken = default) => ExecuteAsync(listingId, ListingAvailabilityOperation.ReplaceWindows, null, null, input,
            () => listings.ReplaceAvailabilityWindowsAsync(listingId, User, input.ToServicePeriods(), cancellationToken), cancellationToken);

    /// <summary>
    /// Creates a private availability block using existing date, reason, and rental-conflict validation.
    /// </summary>
    [HttpPost("blocks")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CreateBlockAsync([FromRoute] int listingId, [FromForm(Name = "")] ListingAvailabilityBlockInputViewModel input,
        CancellationToken cancellationToken = default) => ExecuteAsync(listingId, ListingAvailabilityOperation.CreateBlock, null, input, null,
            () => listings.CreateAvailabilityBlockAsync(listingId, User, input.ToServiceBlock(), cancellationToken), cancellationToken);

    /// <summary>
    /// Updates only the selected block's dates and optional private reason.
    /// </summary>
    [HttpPost("blocks/{blockId:int}/edit")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateBlockAsync([FromRoute] int listingId, [FromRoute] int blockId,
        [FromForm(Name = "")] ListingAvailabilityBlockInputViewModel input, CancellationToken cancellationToken = default) =>
        ExecuteAsync(listingId, ListingAvailabilityOperation.UpdateBlock, blockId, input, null,
            () => listings.UpdateAvailabilityBlockAsync(listingId, blockId, User, input.ToServiceBlock(), cancellationToken), cancellationToken);

    /// <summary>
    /// Removes an owned block while keeping all checks and historical records in the service.
    /// </summary>
    [HttpPost("blocks/{blockId:int}/remove")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RemoveBlockAsync([FromRoute] int listingId, [FromRoute] int blockId,
        CancellationToken cancellationToken = default) => ExecuteAsync(listingId, ListingAvailabilityOperation.RemoveBlock, blockId, null, null,
            () => listings.RemoveAvailabilityBlockAsync(listingId, blockId, User, cancellationToken), cancellationToken);

    /// <summary>
    /// Executes valid form submissions, redirects after success, and redisplays an authorized snapshot after failure.
    /// </summary>
    private async Task<IActionResult> ExecuteAsync<T>(int listingId, ListingAvailabilityOperation operation, int? recordId,
        ListingAvailabilityWindowInputViewModel? input, ListingAvailabilityReplacementViewModel? replacement,
        Func<Task<ListingServiceResult<T>>> submit, CancellationToken cancellationToken)
    {
        var status = ListingServiceStatus.ValidationFailed;
        IReadOnlyList<ListingValidationError> errors = Array.Empty<ListingValidationError>();
        if (ModelState.IsValid)
        {
            var result = await submit();
            if (result.IsSuccess)
            {
                if (IsEditorRequest()) return Json(new { status = "Success" });
                TempData["ListingAvailabilitySuccess"] = operation switch
                {
                    ListingAvailabilityOperation.CreateWindow => "Your availability window was added.",
                    ListingAvailabilityOperation.UpdateWindow => "Your availability window was updated.",
                    ListingAvailabilityOperation.RemoveWindow => "Your availability window was removed.",
                    ListingAvailabilityOperation.ReplaceWindows => "Your availability windows were replaced.",
                    ListingAvailabilityOperation.CreateBlock => "Your unavailable dates were added.",
                    ListingAvailabilityOperation.UpdateBlock => "Your unavailable dates were updated.",
                    _ => "Your unavailable dates were removed."
                };
                return RedirectToAction("Index", new { listingId });
            }
            status = result.Status;
            errors = result.Errors;
        }
        var terminal = TerminalFailure(status);
        if (terminal is not null)
        {
            return terminal;
        }
        foreach (var error in errors)
        {
            ModelState.AddModelError(FormField(error.Field, operation), AvailabilityMessage(error.Message));
        }
        var current = await listings.GetOwnerAvailabilityAsync(listingId, User, cancellationToken);
        if (!current.IsSuccess)
        {
            return ReadFailure(current.Status);
        }
        if (IsEditorRequest())
        {
            return StatusCode(FailureStatusCode(status), new { status = status.ToString(),
                errors = ModelState.SelectMany(pair => pair.Value!.Errors.Select(error => new ListingValidationError(pair.Key, error.ErrorMessage))).ToArray() });
        }
        var feedback = new ListingAvailabilityFeedbackViewModel(operation, recordId, status, errors.ToArray());
        var view = View("Index", new ListingAvailabilityManagementViewModel(current.Value!, input, replacement, feedback));
        view.StatusCode = FailureStatusCode(status);
        return view;
    }

    /// <summary>Identifies a structured response requested by the integrated editor.</summary>
    private bool IsEditorRequest() => Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps service period/group errors to submitted date/collection fields while keeping rental/state errors in the summary.
    /// </summary>
    private static string FormField(string field, ListingAvailabilityOperation operation)
    {
        if (field.StartsWith("AvailabilityWindows", StringComparison.Ordinal))
        {
            return operation == ListingAvailabilityOperation.ReplaceWindows
                ? ("Windows" + field["AvailabilityWindows".Length..]).Replace(".EndDate", ".LastDay", StringComparison.Ordinal)
                : operation is ListingAvailabilityOperation.CreateWindow or ListingAvailabilityOperation.UpdateWindow ? "LastDay" : string.Empty;
        }
        if (field == "AvailabilityBlocks")
        {
            return operation is ListingAvailabilityOperation.CreateBlock or ListingAvailabilityOperation.UpdateBlock ? "LastDay" : string.Empty;
        }
        return field == "EndDate" ? "LastDay" : field is "StartDate" or "Reason" ? field : string.Empty;
    }

    /// <summary>Adapts legacy boundary wording without changing the service validation rule or its result.</summary>
    private static string AvailabilityMessage(string message) => message
        .Replace("with the start before the end", "with Last Day on or after First Day", StringComparison.Ordinal);

    /// <summary>
    /// Maps auth/resource failures and unexpected outcomes before any private block data is returned.
    /// </summary>
    private IActionResult? TerminalFailure(ListingServiceStatus status) => status switch
    {
        ListingServiceStatus.Unauthenticated => Challenge(),
        ListingServiceStatus.Forbidden => Forbid(),
        ListingServiceStatus.NotFound => NotFound(),
        ListingServiceStatus.ValidationFailed or ListingServiceStatus.Conflict or ListingServiceStatus.InvalidState => null,
        _ => Problem(statusCode: FailureStatusCode(status), title: "Availability could not be updated.")
    };

    /// <summary>
    /// Handles read errors without constructing a form from unauthorized or missing data.
    /// </summary>
    private IActionResult ReadFailure(ListingServiceStatus status) => TerminalFailure(status)
        ?? Problem(statusCode: FailureStatusCode(status), title: "Availability could not be loaded.");

    /// <summary>
    /// Distinguishes form validation, conflicts, and unavailable server dependencies.
    /// </summary>
    private static int FailureStatusCode(ListingServiceStatus status) => status switch
    {
        ListingServiceStatus.ValidationFailed => StatusCodes.Status400BadRequest,
        ListingServiceStatus.Conflict or ListingServiceStatus.InvalidState => StatusCodes.Status409Conflict,
        ListingServiceStatus.PersistenceFailed or ListingServiceStatus.StorageFailed => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}
