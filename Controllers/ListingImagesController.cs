using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RentRoad.Services.Listings;
using RentRoad.Services.Storage;
using RentRoad.ViewModels.Listings;

namespace RentRoad.Controllers;

/// <summary>
/// Adapts owner image management to MVC with explicit route IDs and narrow form contracts.
/// </summary>
[Authorize]
[Route("my/listings/{listingId:int}/images")]
public sealed class ListingImagesController : Controller
{
    private const long MAX_UPLOAD_REQUEST_BYTES = BlobImageStorageOptions.MAX_ALLOWED_IMAGE_SIZE_BYTES + 1024 * 1024;
    private readonly IListingImageService images;

    /// <summary>
    /// Initializes owner endpoints with the existing image orchestration service.
    /// </summary>
    public ListingImagesController(IListingImageService images)
    {
        this.images = images;
    }

    /// <summary>
    /// Retrieves the current owner's image metadata without exposing storage references.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> IndexAsync([FromRoute] int listingId, CancellationToken cancellationToken = default)
    {
        var result = await images.GetImagesAsync(listingId, User, cancellationToken);
        return result.IsSuccess
            ? View("Index", new ListingImageManagementViewModel(listingId, result.Value!))
            : TerminalFailure(result.Status) ?? Problem(statusCode: StatusCodeFor(result.Status), title: "Listing images could not be loaded.");
    }

    /// <summary>
    /// Uploads one file through existing byte validation and metadata orchestration, disposing the caller-owned input stream.
    /// </summary>
    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MAX_UPLOAD_REQUEST_BYTES)]
    [RequestFormLimits(MultipartBodyLengthLimit = BlobImageStorageOptions.MAX_ALLOWED_IMAGE_SIZE_BYTES)]
    public async Task<IActionResult> UploadAsync([FromRoute] int listingId, [FromForm(Name = "")] ListingImageUploadViewModel input,
        CancellationToken cancellationToken = default)
    {
        if (input.File is null && ModelState.GetFieldValidationState(nameof(input.File)) != ModelValidationState.Invalid)
        {
            ModelState.AddModelError(nameof(input.File), "Choose an image to upload.");
        }
        if (!ModelState.IsValid || input.File is null)
        {
            return await FailureAsync(listingId, ListingServiceStatus.ValidationFailed, Array.Empty<ListingValidationError>(),
                input, null, null, cancellationToken);
        }
        await using var stream = input.File.OpenReadStream();
        var result = await images.UploadAsync(listingId, User, stream, input.File.ContentType, input.File.FileName, input.AltText, cancellationToken);
        if (result.IsSuccess)
        {
            return RedirectToAction("Index", new { listingId });
        }
        return await FailureAsync(listingId, result.Status, result.Errors, input, null, null, cancellationToken);
    }

    /// <summary>
    /// Removes an owned image through existing consistency checks and explicitly reports incomplete blob cleanup.
    /// </summary>
    [HttpPost("{imageId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAsync([FromRoute] int listingId, [FromRoute] int imageId,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }
        var result = await images.DeleteAsync(listingId, imageId, User, cancellationToken);
        if (result.IsSuccess)
        {
            return RedirectToAction("Index", new { listingId });
        }
        return await FailureAsync(listingId, result.Status, result.Errors, null, null, result.Value, cancellationToken);
    }

    /// <summary>
    /// Selects one owned primary image through the service's transactional unique-primary handling.
    /// </summary>
    [HttpPost("{imageId:int}/primary")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrimaryAsync([FromRoute] int listingId, [FromRoute] int imageId,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }
        var result = await images.SetPrimaryAsync(listingId, imageId, User, cancellationToken);
        if (result.IsSuccess)
        {
            return RedirectToAction("Index", new { listingId });
        }
        return await FailureAsync(listingId, result.Status, result.Errors, null, null, null, cancellationToken);
    }

    /// <summary>
    /// Forwards a requested complete order to existing service validation without binding display positions or image entities.
    /// </summary>
    [HttpPost("order")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDisplayOrderAsync([FromRoute] int listingId, [FromForm(Name = "")] ListingImageOrderViewModel input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || input.ImageIds is null)
        {
            if (input.ImageIds is null && ModelState.GetFieldValidationState(nameof(input.ImageIds)) != ModelValidationState.Invalid)
            {
                ModelState.AddModelError(nameof(input.ImageIds), "Supply the listing's image order.");
            }
            return await FailureAsync(listingId, ListingServiceStatus.ValidationFailed, Array.Empty<ListingValidationError>(),
                null, input, null, cancellationToken);
        }
        var result = await images.SetDisplayOrderAsync(listingId, input.ImageIds, User, cancellationToken);
        if (result.IsSuccess)
        {
            return RedirectToAction("Index", new { listingId });
        }
        return await FailureAsync(listingId, result.Status, result.Errors, null, input, null, cancellationToken);
    }

    /// <summary>
    /// Redisplays authorized metadata and operation state without losing confirmed partial success if refresh fails.
    /// </summary>
    private async Task<IActionResult> FailureAsync(int listingId, ListingServiceStatus status, IReadOnlyList<ListingValidationError> errors,
        ListingImageUploadViewModel? upload, ListingImageOrderViewModel? order, ListingImageDeletion? deletion, CancellationToken cancellationToken)
    {
        var terminal = TerminalFailure(status);
        if (terminal is not null)
        {
            return terminal;
        }
        foreach (var error in errors)
        {
            var field = order is not null && error.Field == "Images" ? nameof(order.ImageIds)
                : upload is not null && status == ListingServiceStatus.ValidationFailed
                    && error.Field is "Images" or "OriginalFileName" or "ContentType" or "SizeBytes" ? nameof(upload.File) : error.Field;
            ModelState.AddModelError(field, error.Message);
        }
        var outcome = new ListingImageOperationViewModel(status, deletion?.MetadataRemoved, deletion?.BlobRemoved, errors.ToArray());
        var current = await images.GetImagesAsync(listingId, User, cancellationToken);
        if (!current.IsSuccess)
        {
            var readFailure = TerminalFailure(current.Status);
            if (readFailure is not null && status is not (ListingServiceStatus.CleanupPending or ListingServiceStatus.ReconciliationRequired))
            {
                return readFailure;
            }
            return Problem(statusCode: status is ListingServiceStatus.CleanupPending or ListingServiceStatus.ReconciliationRequired
                    ? StatusCodes.Status409Conflict : StatusCodeFor(current.Status),
                title: "The image operation did not fully complete and image metadata could not be refreshed.",
                extensions: new Dictionary<string, object?> { ["operation"] = outcome });
        }
        var view = View("Index", new ListingImageManagementViewModel(listingId, current.Value!, upload, order, outcome));
        view.StatusCode = StatusCodeFor(status);
        return view;
    }

    /// <summary>
    /// Handles owner-authentication and missing-resource results before exposing a management ViewModel.
    /// </summary>
    private IActionResult? TerminalFailure(ListingServiceStatus status) => status switch
    {
        ListingServiceStatus.Unauthenticated => Challenge(),
        ListingServiceStatus.Forbidden => Forbid(),
        ListingServiceStatus.NotFound => NotFound(),
        _ => null
    };

    /// <summary>
    /// Maps incomplete/unknown outcomes to conflicts and unavailable dependencies to retryable server responses.
    /// </summary>
    private static int StatusCodeFor(ListingServiceStatus status) => status switch
    {
        ListingServiceStatus.ValidationFailed => StatusCodes.Status400BadRequest,
        ListingServiceStatus.Conflict or ListingServiceStatus.InvalidState or ListingServiceStatus.CleanupPending
            or ListingServiceStatus.ReconciliationRequired => StatusCodes.Status409Conflict,
        ListingServiceStatus.Unauthenticated => StatusCodes.Status401Unauthorized,
        ListingServiceStatus.Forbidden => StatusCodes.Status403Forbidden,
        ListingServiceStatus.NotFound => StatusCodes.Status404NotFound,
        ListingServiceStatus.StorageFailed or ListingServiceStatus.PersistenceFailed => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}
