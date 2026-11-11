using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RentRoad.Services.Listings;
using RentRoad.Services.Storage;
using RentRoad.ViewModels.Listings;

namespace RentRoad.Controllers;

/// <summary>Adapts the unified editor to existing owner services, with narrow inputs and private presentation projections.</summary>
[Authorize]
[Route("my/listings")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ListingEditorController(ListingEditorService editor, IListingService listings, IOptions<BlobImageStorageOptions> storageOptions) : Controller
{
    /// <summary>Reads or resumes editor metadata; this GET never creates an empty listing.</summary>
    [HttpGet("editor/session")]
    public async Task<IActionResult> SessionAsync([FromQuery] int? listingId, [FromQuery] string? token, CancellationToken cancellationToken)
    {
        var result = await editor.OpenAsync(User, listingId, token, cancellationToken);
        return result.IsSuccess ? Json(new { state = ListingEditorStateViewModel.FromState(result.Value!), maxImageSizeBytes = storageOptions.Value.MaxImageSizeBytes }) : Failure(result.Status, result.Errors);
    }

    /// <summary>Saves only editable fields through an owner-bound, retry-safe draft receipt.</summary>
    [HttpPost("editor/{token}/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAsync([FromRoute] string token, [FromForm(Name = "")] ListingFormViewModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BindingFailure();
        var result = await editor.SaveAsync(token, User, input.ToServiceUpdate(), cancellationToken);
        return result.IsSuccess ? Json(new { state = ListingEditorStateViewModel.FromState(result.Value!) }) : Failure(result.Status, result.Errors);
    }

    /// <summary>Prepares publication review without publishing or overwriting an existing listing's scalar values.</summary>
    [HttpPost("editor/{token}/review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewAsync([FromRoute] string token, [FromForm(Name = "")] ListingFormViewModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BindingFailure();
        var result = await editor.ReviewAsync(token, User, input.ToServiceUpdate(), cancellationToken);
        return result.IsSuccess ? Json(new { review = ListingEditorReviewViewModel.FromReview(result.Value!) }) : Failure(result.Status, result.Errors);
    }

    /// <summary>Requires the server-issued review receipt before existing service validation can publish.</summary>
    [HttpPost("editor/{token}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmAsync([FromRoute] string token, [FromForm] string nonce, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BindingFailure();
        var result = await editor.ConfirmAsync(token, nonce, User, cancellationToken);
        if (!result.IsSuccess) return Failure(result.Status, result.Errors);
        if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return Json(new { state = ListingEditorStateViewModel.FromState(result.Value!) });
        TempData["ListingSuccess"] = "Your listing is published and visible in Browse Listings.";
        return RedirectToAction("Edit", "MyListings", new { id = result.Value!.Listing!.ListingId });
    }

    /// <summary>Provides an owner-only confirmation page for existing saved-listing publication links.</summary>
    [HttpGet("{id:int}/review")]
    public async Task<IActionResult> SavedReviewAsync([FromRoute] int id, CancellationToken cancellationToken)
    {
        var owned = await listings.GetOwnerListingAsync(id, User, cancellationToken);
        if (!owned.IsSuccess) return Failure(owned.Status, owned.Errors);
        var opened = await editor.OpenAsync(User, id, null, cancellationToken);
        if (!opened.IsSuccess) return Failure(opened.Status, opened.Errors);
        var result = await editor.ReviewAsync(opened.Value!.Token, User, ListingFormViewModel.FromListing(owned.Value!).ToServiceUpdate(), cancellationToken);
        return result.IsSuccess ? View("~/Views/MyListings/Review.cshtml", ListingEditorReviewViewModel.FromReview(result.Value!)) : Failure(result.Status, result.Errors);
    }

    /// <summary>Returns binder errors without replacing the client's current form values.</summary>
    private IActionResult BindingFailure() => Failure(ListingServiceStatus.ValidationFailed, ModelState.SelectMany(pair => pair.Value!.Errors
        .Select(error => new ListingValidationError(pair.Key, string.IsNullOrEmpty(error.ErrorMessage) ? "Enter a valid value." : error.ErrorMessage))).ToArray());

    /// <summary>Maps expected failures to safe structured feedback without returning entities or storage configuration.</summary>
    private ObjectResult Failure(ListingServiceStatus status, IReadOnlyList<ListingValidationError> errors) => StatusCode(status switch
    {
        ListingServiceStatus.Unauthenticated => 401,
        ListingServiceStatus.Forbidden => 403,
        ListingServiceStatus.NotFound => 404,
        ListingServiceStatus.ValidationFailed => 400,
        ListingServiceStatus.PersistenceFailed or ListingServiceStatus.StorageFailed => 503,
        _ => 409
    }, new { status = status.ToString(), errors });
}
