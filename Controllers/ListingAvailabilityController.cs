using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Services.Listings;
using RentRoad.ViewModels.Listings;

namespace RentRoad.Controllers;

/// <summary>
/// Adapts owner availability management to MVC while delegating date, rental, and publication rules to the
/// listing service.
/// </summary>
[Authorize]
[Route("my/listings/{listingId:int}/availability")]
public sealed class ListingAvailabilityController : Controller
{
    private readonly IListingService listings;

    /// <summary>
    /// Initializes availability MVC with the existing listing service.
    /// </summary>
    /// <param name="listings">The service used to retrieve and manage listing information.</param>
    public ListingAvailabilityController(IListingService listings)
    {
        this.listings = listings;
    }

    /// <summary>
    /// Retrieves the owned listing's windows, private blocks, and trusted lifecycle status.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpGet("")]
    public async Task<IActionResult> IndexAsync(
        [FromRoute] int listingId,
        CancellationToken cancellationToken = default)
    {
        var result = await listings.GetOwnerAvailabilityAsync(listingId, User, cancellationToken);

        return result.IsSuccess
            ? View("Index", new ListingAvailabilityManagementViewModel(result.Value!))
            : ReadFailure(result.Status);
    }

    /// <summary>
    /// Creates a window using the existing owner's date and reservation checks.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="input">The submitted availability window dates.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("windows")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CreateWindowAsync(
        [FromRoute] int listingId,
        [FromForm(Name = "")] ListingAvailabilityWindowInputViewModel input,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            listingId,
            ListingAvailabilityOperation.CreateWindow,
            null,
            input,
            null,
            () => listings.CreateAvailabilityWindowAsync(listingId, User, input.ToServicePeriod(), cancellationToken),
            cancellationToken);

    /// <summary>
    /// Updates only the route-selected window's dates through the existing service.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="windowId">The identifier of the availability window selected by the route.</param>
    /// <param name="input">The submitted availability window dates.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("windows/{windowId:int}/edit")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateWindowAsync(
        [FromRoute] int listingId,
        [FromRoute] int windowId,
        [FromForm(Name = "")] ListingAvailabilityWindowInputViewModel input,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            listingId,
            ListingAvailabilityOperation.UpdateWindow,
            windowId,
            input,
            null,
            () => listings.UpdateAvailabilityWindowAsync(listingId, windowId, User, input.ToServicePeriod(), cancellationToken),
            cancellationToken);

    /// <summary>
    /// Removes the selected owned window only when the service permits the resulting configuration.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="windowId">The identifier of the availability window selected by the route.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("windows/{windowId:int}/remove")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RemoveWindowAsync(
        [FromRoute] int listingId,
        [FromRoute] int windowId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            listingId,
            ListingAvailabilityOperation.RemoveWindow,
            windowId,
            null,
            null,
            () => listings.RemoveAvailabilityWindowAsync(listingId, windowId, User, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Replaces all windows without binding or copying client-supplied listing scalar fields.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="input">The submitted replacement availability windows.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("windows/replace")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ReplaceWindowsAsync(
        [FromRoute] int listingId,
        [FromForm(Name = "")] ListingAvailabilityReplacementViewModel input,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            listingId,
            ListingAvailabilityOperation.ReplaceWindows,
            null,
            null,
            input,
            () => listings.ReplaceAvailabilityWindowsAsync(listingId, User, input.ToServicePeriods(), cancellationToken),
            cancellationToken);

    /// <summary>
    /// Creates a private availability block using existing date, reason, and rental-conflict validation.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="input">The submitted block dates and optional private reason.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("blocks")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CreateBlockAsync(
        [FromRoute] int listingId,
        [FromForm(Name = "")] ListingAvailabilityBlockInputViewModel input,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            listingId,
            ListingAvailabilityOperation.CreateBlock,
            null,
            input,
            null,
            () => listings.CreateAvailabilityBlockAsync(listingId, User, input.ToServiceBlock(), cancellationToken),
            cancellationToken);

    /// <summary>
    /// Updates only the selected block's dates and optional private reason.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="blockId">The identifier of the availability block selected by the route.</param>
    /// <param name="input">The submitted block dates and optional private reason.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("blocks/{blockId:int}/edit")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateBlockAsync(
        [FromRoute] int listingId,
        [FromRoute] int blockId,
        [FromForm(Name = "")] ListingAvailabilityBlockInputViewModel input,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            listingId,
            ListingAvailabilityOperation.UpdateBlock,
            blockId,
            input,
            null,
            () => listings.UpdateAvailabilityBlockAsync(listingId, blockId, User, input.ToServiceBlock(), cancellationToken),
            cancellationToken);

    /// <summary>
    /// Removes an owned block while keeping all checks and historical records in the service.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="blockId">The identifier of the availability block selected by the route.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("blocks/{blockId:int}/remove")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RemoveBlockAsync(
        [FromRoute] int listingId,
        [FromRoute] int blockId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            listingId,
            ListingAvailabilityOperation.RemoveBlock,
            blockId,
            null,
            null,
            () => listings.RemoveAvailabilityBlockAsync(listingId, blockId, User, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Executes valid form submissions, redirects after success, and redisplays an authorized snapshot after failure.
    /// </summary>
    /// <typeparam name="T">The value type returned by the service operation.</typeparam>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="operation">The availability operation being performed.</param>
    /// <param name="recordId">The optional identifier of the availability record being modified.</param>
    /// <param name="input">The submitted availability window dates.</param>
    /// <param name="replacement">The optional replacement availability form data.</param>
    /// <param name="submit">The asynchronous availability service operation to execute.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    private async Task<IActionResult> ExecuteAsync<T>(
        int listingId,
        ListingAvailabilityOperation operation,
        int? recordId,
        ListingAvailabilityWindowInputViewModel? input,
        ListingAvailabilityReplacementViewModel? replacement,
        Func<Task<ListingServiceResult<T>>> submit,
        CancellationToken cancellationToken)
    {
        var status = ListingServiceStatus.ValidationFailed;
        IReadOnlyList<ListingValidationError> errors = Array.Empty<ListingValidationError>();

        if (ModelState.IsValid)
        {
            var result = await submit();

            if (result.IsSuccess)
            {
                if (IsEditorRequest())
                {
                    return Json(new { status = "Success" });
                }

                TempData["ListingAvailabilitySuccess"] = operation switch
                {
                    ListingAvailabilityOperation.CreateWindow =>
                        SharedResource.Get("AvailabilityFeedback.YourAvailabilityWindowWasAdded"),
                    ListingAvailabilityOperation.UpdateWindow =>
                        SharedResource.Get("AvailabilityFeedback.YourAvailabilityWindowWasUpdated"),
                    ListingAvailabilityOperation.RemoveWindow =>
                        SharedResource.Get("AvailabilityFeedback.YourAvailabilityWindowWasRemoved"),
                    ListingAvailabilityOperation.ReplaceWindows =>
                        SharedResource.Get("AvailabilityFeedback.YourAvailabilityWindowsWereReplaced"),
                    ListingAvailabilityOperation.CreateBlock =>
                        SharedResource.Get("AvailabilityFeedback.YourUnavailableDatesWereAdded"),
                    ListingAvailabilityOperation.UpdateBlock =>
                        SharedResource.Get("AvailabilityFeedback.YourUnavailableDatesWereUpdated"),
                    _ => SharedResource.Get("AvailabilityFeedback.YourUnavailableDatesWereRemoved")
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
            return StatusCode(
                FailureStatusCode(status),
                new
                {
                    status = status.ToString(),
                    errors = ModelState
                        .SelectMany(pair => pair.Value!.Errors.Select(
                            error => new ListingValidationError(pair.Key, error.ErrorMessage)))
                        .ToArray()
                });
        }

        var feedback = new ListingAvailabilityFeedbackViewModel(operation, recordId, status, errors.ToArray());
        var view = View(
            "Index",
            new ListingAvailabilityManagementViewModel(current.Value!, input, replacement, feedback));

        view.StatusCode = FailureStatusCode(status);
        return view;
    }

    /// <summary>
    /// Identifies a structured response requested by the integrated editor.
    /// </summary>
    /// <returns>Whether the request accepts a structured JSON response.</returns>
    private bool IsEditorRequest() =>
        Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps service period/group errors to submitted date/collection fields while keeping rental/state
    /// errors in the summary.
    /// </summary>
    /// <param name="field">The field name returned by service validation.</param>
    /// <param name="operation">The availability operation being performed.</param>
    /// <returns>The matching editable form field name, or an empty name for summary errors.</returns>
    private static string FormField(string field, ListingAvailabilityOperation operation)
    {
        if (field.StartsWith("AvailabilityWindows", StringComparison.Ordinal))
        {
            return operation == ListingAvailabilityOperation.ReplaceWindows
                ? ("Windows" + field["AvailabilityWindows".Length..]).Replace(".EndDate", ".LastDay", StringComparison.Ordinal)
                : operation is ListingAvailabilityOperation.CreateWindow or ListingAvailabilityOperation.UpdateWindow
                    ? "LastDay"
                    : string.Empty;
        }

        if (field == "AvailabilityBlocks")
        {
            return operation is ListingAvailabilityOperation.CreateBlock or ListingAvailabilityOperation.UpdateBlock
                ? "LastDay"
                : string.Empty;
        }

        return field == "EndDate" ? "LastDay" : field is "StartDate" or "Reason" ? field : string.Empty;
    }

    /// <summary>
    /// Selects complete inclusive-input messages without rewriting translated sentence fragments.
    /// </summary>
    /// <param name="message">The validation message returned by the availability service.</param>
    /// <returns>The matching inclusive-date validation message, or the original message.</returns>
    private static string AvailabilityMessage(string message) =>
        message == SharedResource.Get("ListingValidation.ChooseAvailabilityDatesWithTheStartBeforeTheEnd")
            ? SharedResource.Get("Availability.InvalidWindowDates")
            : message == SharedResource.Get("AvailabilityFeedback.ChooseUnavailableDatesWithTheStartBeforeTheEnd")
                ? SharedResource.Get("Availability.InvalidBlockDates")
                : message;

    /// <summary>
    /// Maps auth/resource failures and unexpected outcomes before any private block data is returned.
    /// </summary>
    /// <param name="status">The service outcome to translate into an MVC response.</param>
    /// <returns>The terminal MVC failure response, or null when the caller should handle the outcome.</returns>
    private IActionResult? TerminalFailure(ListingServiceStatus status) =>
        status switch
        {
            ListingServiceStatus.Unauthenticated => Challenge(),
            ListingServiceStatus.Forbidden => Forbid(),
            ListingServiceStatus.NotFound => NotFound(),
            ListingServiceStatus.ValidationFailed or ListingServiceStatus.Conflict or ListingServiceStatus.InvalidState =>
                null,
            _ =>
                Problem(
                    statusCode: FailureStatusCode(status),
                    title: SharedResource.Get("AvailabilityFeedback.AvailabilityCouldNotBeUpdated"))
        };

    /// <summary>
    /// Handles read errors without constructing a form from unauthorized or missing data.
    /// </summary>
    /// <param name="status">The service outcome to translate into an MVC response.</param>
    /// <returns>The MVC response for the operation's outcome.</returns>
    private IActionResult ReadFailure(ListingServiceStatus status) =>
        TerminalFailure(status)
            ?? Problem(
                statusCode: FailureStatusCode(status),
                title: SharedResource.Get("AvailabilityFeedback.AvailabilityCouldNotBeLoaded"));

    /// <summary>
    /// Distinguishes form validation, conflicts, and unavailable server dependencies.
    /// </summary>
    /// <param name="status">The service outcome to translate into an MVC response.</param>
    /// <returns>The HTTP status code for the service outcome.</returns>
    private static int FailureStatusCode(ListingServiceStatus status) =>
        status switch
        {
            ListingServiceStatus.ValidationFailed => StatusCodes.Status400BadRequest,
            ListingServiceStatus.Conflict or ListingServiceStatus.InvalidState => StatusCodes.Status409Conflict,
            ListingServiceStatus.PersistenceFailed or ListingServiceStatus.StorageFailed =>
                StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
}
