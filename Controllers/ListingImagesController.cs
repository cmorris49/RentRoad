using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using RentRoad.Models;
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
    private readonly IListingService listings;
    private readonly int maxImageSizeBytes;

    /// <summary>
    /// Initializes owner endpoints with image operations, authorized listing context, and the configured upload limit.
    /// </summary>
    /// <param name="images">The service used to authorize and manage listing images.</param>
    /// <param name="listings">The service used to retrieve and manage listing information.</param>
    /// <param name="storageOptions">The storage options containing the configured image size limit.</param>
    public ListingImagesController(
        IListingImageService images,
        IListingService listings,
        IOptions<BlobImageStorageOptions> storageOptions)
    {
        this.images = images;
        this.listings = listings;
        maxImageSizeBytes = storageOptions.Value.MaxImageSizeBytes;
    }

    /// <summary>
    /// Retrieves the current owner's image metadata without exposing storage references.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpGet("")]
    public async Task<IActionResult> IndexAsync(
        [FromRoute] int listingId,
        CancellationToken cancellationToken = default)
    {
        var result = await images.GetImagesAsync(listingId, User, cancellationToken);

        return result.IsSuccess
            ? await ManagementViewAsync(listingId, result.Value!, null, null, null, null, cancellationToken)
            : TerminalFailure(result.Status)
                ?? Problem(
                    statusCode: StatusCodeFor(result.Status),
                    title: SharedResource.Get("ImageFeedback.ListingImagesCouldNotBeLoaded"));
    }

    /// <summary>
    /// Uploads one file through existing byte validation and metadata orchestration, disposing the
    /// caller-owned input stream.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="input">The submitted image file and optional alternative text.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MAX_UPLOAD_REQUEST_BYTES)]
    [RequestFormLimits(MultipartBodyLengthLimit = BlobImageStorageOptions.MAX_ALLOWED_IMAGE_SIZE_BYTES)]
    public async Task<IActionResult> UploadAsync(
        [FromRoute] int listingId,
        [FromForm(Name = "")] ListingImageUploadViewModel input,
        CancellationToken cancellationToken = default)
    {
        if (input.File is null
            && ModelState.GetFieldValidationState(nameof(input.File)) != ModelValidationState.Invalid)
        {
            ModelState.AddModelError(nameof(input.File), SharedResource.Get("ListingImages.FileRequired"));
        }

        if (!ModelState.IsValid || input.File is null)
        {
            return await FailureAsync(
                listingId,
                ListingServiceStatus.ValidationFailed,
                Array.Empty<ListingValidationError>(),
                input,
                null,
                null,
                cancellationToken);
        }

        await using var stream = input.File.OpenReadStream();
        var result = await images.UploadAsync(
            listingId,
            User,
            stream,
            input.File.ContentType,
            input.File.FileName,
            input.AltText,
            cancellationToken);

        if (result.IsSuccess)
        {
            if (IsEditorRequest())
            {
                return Json(new { status = "Success" });
            }

            TempData["ListingImageSuccess"] =
                SharedResource.Get("ImageFeedback.YourImageWasUploadedAndAddedToTheListing");
            return RedirectToAction("Index", new { listingId });
        }

        return await FailureAsync(listingId, result.Status, result.Errors, input, null, null, cancellationToken);
    }

    /// <summary>
    /// Removes an owned image through existing consistency checks and explicitly reports incomplete blob cleanup.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="imageId">The identifier of the listing image selected by the route.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("{imageId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAsync(
        [FromRoute] int listingId,
        [FromRoute] int imageId,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(modelStateDictionary: ModelState, title: SharedResource.Get("Validation.Summary"));
        }

        var result = await images.DeleteAsync(listingId, imageId, User, cancellationToken);

        if (result.IsSuccess)
        {
            if (IsEditorRequest())
            {
                return Json(new { status = "Success" });
            }

            TempData["ListingImageSuccess"] =
                SharedResource.Get("ImageFeedback.TheImageWasRemovedFromYourListingAndStorage");
            return RedirectToAction("Index", new { listingId });
        }

        return await FailureAsync(listingId, result.Status, result.Errors, null, null, result.Value, cancellationToken);
    }

    /// <summary>
    /// Selects one owned primary image through the service's transactional unique-primary handling.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="imageId">The identifier of the listing image selected by the route.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("{imageId:int}/primary")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrimaryAsync(
        [FromRoute] int listingId,
        [FromRoute] int imageId,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(modelStateDictionary: ModelState, title: SharedResource.Get("Validation.Summary"));
        }

        var result = await images.SetPrimaryAsync(listingId, imageId, User, cancellationToken);

        if (result.IsSuccess)
        {
            if (IsEditorRequest())
            {
                return Json(new { status = "Success" });
            }

            TempData["ListingImageSuccess"] = SharedResource.Get("ImageFeedback.YourPrimaryImageHasBeenUpdated");
            return RedirectToAction("Index", new { listingId });
        }

        return await FailureAsync(listingId, result.Status, result.Errors, null, null, null, cancellationToken);
    }

    /// <summary>
    /// Forwards a requested complete order to existing service validation without binding display positions
    /// or image entities.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="input">The submitted complete sequence of listing image identifiers.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [HttpPost("order")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDisplayOrderAsync(
        [FromRoute] int listingId,
        [FromForm(Name = "")] ListingImageOrderViewModel input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || input.ImageIds is null)
        {
            if (input.ImageIds is null
                && ModelState.GetFieldValidationState(nameof(input.ImageIds)) != ModelValidationState.Invalid)
            {
                ModelState.AddModelError(nameof(input.ImageIds), SharedResource.Get("ListingImages.OrderRequired"));
            }

            return await FailureAsync(
                listingId,
                ListingServiceStatus.ValidationFailed,
                Array.Empty<ListingValidationError>(),
                null,
                input,
                null,
                cancellationToken);
        }

        var result = await images.SetDisplayOrderAsync(listingId, input.ImageIds, User, cancellationToken);

        if (result.IsSuccess)
        {
            if (IsEditorRequest())
            {
                return Json(new { status = "Success" });
            }

            TempData["ListingImageSuccess"] = SharedResource.Get("ImageFeedback.YourImageDisplayOrderHasBeenSaved");
            return RedirectToAction("Index", new { listingId });
        }

        return await FailureAsync(listingId, result.Status, result.Errors, null, input, null, cancellationToken);
    }

    /// <summary>
    /// Redisplays authorized metadata and operation state without losing confirmed partial success if refresh fails.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="status">The service outcome to translate into an MVC response.</param>
    /// <param name="errors">The validation or operation errors returned by the service.</param>
    /// <param name="upload">The optional submitted image upload values to redisplay.</param>
    /// <param name="order">The optional submitted image order to redisplay.</param>
    /// <param name="deletion">The optional confirmed metadata and blob deletion outcome.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    private async Task<IActionResult> FailureAsync(
        int listingId,
        ListingServiceStatus status,
        IReadOnlyList<ListingValidationError> errors,
        ListingImageUploadViewModel? upload,
        ListingImageOrderViewModel? order,
        ListingImageDeletion? deletion,
        CancellationToken cancellationToken)
    {
        var terminal = TerminalFailure(status);

        if (terminal is not null)
        {
            return terminal;
        }

        foreach (var error in errors)
        {
            var field = order is not null && error.Field == "Images"
                ? nameof(order.ImageIds)
                : upload is not null && status == ListingServiceStatus.ValidationFailed
                    && error.Field is "Images" or "OriginalFileName" or "ContentType" or "SizeBytes"
                    ? nameof(upload.File)
                    : error.Field;

            ModelState.AddModelError(field, error.Message);
        }

        var outcome = new ListingImageOperationViewModel(
            status,
            deletion?.MetadataRemoved,
            deletion?.BlobRemoved,
            errors.ToArray());
        var current = await images.GetImagesAsync(listingId, User, cancellationToken);

        if (!current.IsSuccess)
        {
            if (IsEditorRequest()
                && status is ListingServiceStatus.CleanupPending or ListingServiceStatus.ReconciliationRequired)
            {
                return StatusCode(
                    StatusCodes.Status409Conflict,
                    new
                    {
                        status = status.ToString(),
                        errors = outcome.Errors,
                        metadataRemoved = outcome.MetadataRemoved,
                        blobRemoved = outcome.BlobRemoved
                    });
            }

            var readFailure = TerminalFailure(current.Status);

            if (readFailure is not null
                && status is not (ListingServiceStatus.CleanupPending or ListingServiceStatus.ReconciliationRequired))
            {
                return readFailure;
            }

            return Problem(
                statusCode: status is ListingServiceStatus.CleanupPending or ListingServiceStatus.ReconciliationRequired
                    ? StatusCodes.Status409Conflict
                    : StatusCodeFor(current.Status),
                title: SharedResource.Get("ImageFeedback.TheImageOperationDidNotFullyCompleteAndImage"),
                extensions: new Dictionary<string, object?>
                {
                    ["operation"] = outcome
                });
        }

        if (IsEditorRequest())
        {
            return StatusCode(
                StatusCodeFor(status),
                new
                {
                    status = status.ToString(),
                    errors = ModelState
                        .SelectMany(pair => pair.Value!.Errors.Select(
                            error => new ListingValidationError(pair.Key, error.ErrorMessage)))
                        .ToArray(),
                    metadataRemoved = deletion?.MetadataRemoved,
                    blobRemoved = deletion?.BlobRemoved
                });
        }

        return await ManagementViewAsync(
            listingId,
            current.Value!,
            upload,
            order,
            outcome,
            StatusCodeFor(status),
            cancellationToken);
    }

    /// <summary>
    /// Identifies a structured editor request without changing standalone owner routes.
    /// </summary>
    /// <returns>Whether the request accepts a structured JSON response.</returns>
    private bool IsEditorRequest() =>
        Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds separately authorized listing context and the configured byte limit without exposing Identity
    /// or storage configuration.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="metadata">The authorized image metadata to display for the listing.</param>
    /// <param name="upload">The optional submitted image upload values to redisplay.</param>
    /// <param name="order">The optional submitted image order to redisplay.</param>
    /// <param name="outcome">The optional image operation outcome to display.</param>
    /// <param name="statusCode">The HTTP status to apply to the response, when supplied.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    private async Task<IActionResult> ManagementViewAsync(
        int listingId,
        IReadOnlyList<ListingImage> metadata,
        ListingImageUploadViewModel? upload,
        ListingImageOrderViewModel? order,
        ListingImageOperationViewModel? outcome,
        int? statusCode,
        CancellationToken cancellationToken)
    {
        var listing = await listings.GetOwnerListingAsync(listingId, User, cancellationToken);

        if (!listing.IsSuccess)
        {
            if (outcome?.Status is ListingServiceStatus.CleanupPending or ListingServiceStatus.ReconciliationRequired)
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: SharedResource.Get("ImageFeedback.TheImageOperationDidNotFullyCompleteAndListing"),
                    extensions: new Dictionary<string, object?>
                    {
                        ["operation"] = outcome
                    });
            }

            return TerminalFailure(listing.Status)
                ?? Problem(
                    statusCode: StatusCodeFor(listing.Status),
                    title: SharedResource.Get("ListingFeedback.TheListingCouldNotBeLoaded"));
        }

        var view = View(
            "Index",
            new ListingImageManagementViewModel(listingId, metadata, upload, order, outcome)
            {
                ListingTitle = listing.Value!.Title,
                Status = listing.Value.Status,
                MaxImageSizeBytes = maxImageSizeBytes
            });

        view.StatusCode = statusCode;
        return view;
    }

    /// <summary>
    /// Handles owner-authentication and missing-resource results before exposing a management ViewModel.
    /// </summary>
    /// <param name="status">The service outcome to translate into an MVC response.</param>
    /// <returns>The terminal MVC failure response, or null when the caller should handle the outcome.</returns>
    private IActionResult? TerminalFailure(ListingServiceStatus status) =>
        status switch
        {
            ListingServiceStatus.Unauthenticated => Challenge(),
            ListingServiceStatus.Forbidden => Forbid(),
            ListingServiceStatus.NotFound => NotFound(),
            _ => null
        };

    /// <summary>
    /// Maps incomplete/unknown outcomes to conflicts and unavailable dependencies to retryable server responses.
    /// </summary>
    /// <param name="status">The service outcome to translate into an MVC response.</param>
    /// <returns>The HTTP status code for the service outcome.</returns>
    private static int StatusCodeFor(ListingServiceStatus status) =>
        status switch
        {
            ListingServiceStatus.ValidationFailed => StatusCodes.Status400BadRequest,
            ListingServiceStatus.Conflict or ListingServiceStatus.InvalidState
                or ListingServiceStatus.CleanupPending or ListingServiceStatus.ReconciliationRequired =>
                StatusCodes.Status409Conflict,
            ListingServiceStatus.Unauthenticated => StatusCodes.Status401Unauthorized,
            ListingServiceStatus.Forbidden => StatusCodes.Status403Forbidden,
            ListingServiceStatus.NotFound => StatusCodes.Status404NotFound,
            ListingServiceStatus.StorageFailed or ListingServiceStatus.PersistenceFailed =>
                StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
}
