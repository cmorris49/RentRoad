using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Models;
using RentRoad.Services.Listings;
using RentRoad.ViewModels.Listings;

namespace RentRoad.Controllers;

/// <summary>
/// Adapts authenticated owner listing management to MVC without exposing entity form binding or database operations.
/// </summary>
[Authorize]
[Route("my/listings")]
public sealed class MyListingsController : Controller
{
    private readonly IListingService listings;

    /// <summary>
    /// Initializes owner management using the existing listing service and Identity principal supplied by MVC.
    /// </summary>
    public MyListingsController(IListingService listings)
    {
        this.listings = listings;
    }

    /// <summary>
    /// Displays all current-user listings, including drafts, unavailable items, and archived history.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> IndexAsync(CancellationToken cancellationToken = default)
    {
        var result = await listings.GetOwnerListingsAsync(User, cancellationToken);
        return result.IsSuccess
            ? View("Index", new OwnerListingManagementViewModel(result.Value!))
            : ReadFailure(result.Status);
    }

    /// <summary>
    /// Displays an incomplete create form without creating any database records.
    /// </summary>
    [HttpGet("create")]
    public Task<IActionResult> CreateAsync() => Task.FromResult<IActionResult>(View("Create", new ListingCreateViewModel()));

    /// <summary>
    /// Creates a validated draft in one save and redirects to editing after success.
    /// </summary>
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAsync([FromForm(Name = "")] ListingCreateViewModel input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return FormView("Create", input, StatusCodes.Status400BadRequest);
        }
        var result = await listings.CreateDraftAsync(User, input.ToServiceUpdate(), cancellationToken);
        if (result.IsSuccess)
        {
            TempData["ListingSuccess"] = SharedResource.Get("ListingFeedback.YourDraftHasBeenSavedYouCanKeepEditing");
            return RedirectToAction("Edit", new { id = result.Value!.ListingId });
        }
        var terminal = TerminalFailure(result.Status);
        if (terminal is not null)
        {
            return terminal;
        }
        AddErrors(result.Errors);
        return FormView("Create", input, FailureStatusCode(result.Status));
    }

    /// <summary>
    /// Displays one owned listing without validating publication or loading all of the owner's listings.
    /// </summary>
    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> EditAsync([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        var result = await listings.GetOwnerListingAsync(id, User, cancellationToken);
        return result.IsSuccess
            ? View("Edit", new ListingEditViewModel(result.Value!))
            : ReadFailure(result.Status);
    }

    /// <summary>
    /// Saves only editable scalar fields for the route-selected listing and preserves submitted values on failure.
    /// </summary>
    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAsync([FromRoute] int id, [FromForm(Name = "")] ListingFormViewModel input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return await EditFailureAsync(id, input, StatusCodes.Status400BadRequest, null, cancellationToken);
        }
        var result = await listings.UpdateAsync(id, User, input.ToServiceUpdate(), cancellationToken);
        if (result.IsSuccess)
        {
            TempData["ListingSuccess"] = SharedResource.Get("ListingFeedback.YourListingChangesHaveBeenSaved");
            return RedirectToAction("Edit", new { id = result.Value!.ListingId });
        }
        var terminal = TerminalFailure(result.Status);
        if (terminal is not null)
        {
            return terminal;
        }
        AddErrors(result.Errors);
        return await EditFailureAsync(id, input, FailureStatusCode(result.Status), null, cancellationToken);
    }

    /// <summary>
    /// Opens an owner-only review step; this endpoint never publishes without final confirmation.
    /// </summary>
    [HttpPost("{id:int}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishAsync([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(modelStateDictionary: ModelState, title: SharedResource.Get("Validation.Summary"));
        }
        var result = await listings.GetOwnerListingAsync(id, User, cancellationToken);
        return result.IsSuccess ? RedirectToAction("SavedReview", "ListingEditor", new { id }) : ReadFailure(result.Status);
    }

    /// <summary>
    /// Archives the owned listing through the service and redirects to management without deleting historical records.
    /// </summary>
    [HttpPost("{id:int}/archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ArchiveAsync([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(modelStateDictionary: ModelState, title: SharedResource.Get("Validation.Summary"));
        }
        var result = await listings.ArchiveAsync(id, User, cancellationToken);
        if (result.IsSuccess)
        {
            TempData["ListingSuccess"] = SharedResource.Get("ListingFeedback.YourListingHasBeenArchivedItsHistoryIsPreserved");
            return RedirectToAction("Index");
        }
        var terminal = TerminalFailure(result.Status);
        if (terminal is not null)
        {
            return terminal;
        }
        AddErrors(result.Errors);
        return await EditFailureAsync(id, null, FailureStatusCode(result.Status), null, cancellationToken);
    }

    /// <summary>
    /// Reloads trusted status after failure while retaining submitted fields and existing ModelState attempted values.
    /// </summary>
    private async Task<IActionResult> EditFailureAsync(int id, ListingFormViewModel? input, int statusCode,
        IReadOnlyList<ListingValidationError>? publicationErrors, CancellationToken cancellationToken)
    {
        var current = await listings.GetOwnerListingAsync(id, User, cancellationToken);
        return current.IsSuccess
            ? FormView("Edit", new ListingEditViewModel(current.Value!, input, publicationErrors), statusCode)
            : ReadFailure(current.Status);
    }

    /// <summary>
    /// Maps known resource/authentication failures without including private service diagnostics in server errors.
    /// </summary>
    private IActionResult? TerminalFailure(ListingServiceStatus status) => status switch
    {
        ListingServiceStatus.Unauthenticated => Challenge(),
        ListingServiceStatus.Forbidden => Forbid(),
        ListingServiceStatus.NotFound => NotFound(),
        ListingServiceStatus.ValidationFailed or ListingServiceStatus.Conflict or ListingServiceStatus.InvalidState => null,
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: SharedResource.Get("ListingFeedback.TheListingOperationCouldNotBeCompleted"))
    };

    /// <summary>
    /// Handles read failures for which there is no authorized form model to redisplay.
    /// </summary>
    private IActionResult ReadFailure(ListingServiceStatus status) => TerminalFailure(status)
        ?? Problem(statusCode: FailureStatusCode(status), title: SharedResource.Get("ListingFeedback.TheListingCouldNotBeLoaded"));

    /// <summary>
    /// Adds service field errors to the same flat field names used by the form ViewModels.
    /// </summary>
    private void AddErrors(IReadOnlyList<ListingValidationError> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(error.Field, error.Message);
        }
    }

    /// <summary>
    /// Distinguishes invalid input from conflicting or disallowed lifecycle operations.
    /// </summary>
    private static int FailureStatusCode(ListingServiceStatus status) => status == ListingServiceStatus.ValidationFailed
        ? StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict;

    /// <summary>
    /// Returns a typed form response with the intended failure status, without redirecting or clearing submitted values.
    /// </summary>
    private ViewResult FormView(string name, object model, int statusCode)
    {
        var result = View(name, model);
        result.StatusCode = statusCode;
        return result;
    }
}
