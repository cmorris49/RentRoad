using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Services.Reviews;
using RentRoad.ViewModels.Reviews;

namespace RentRoad.Controllers;

/// <summary>
/// Accepts authenticated review submissions and exposes a simple listing review summary endpoint.
/// </summary>
[Route("reviews")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReviewsController : Controller
{
    private readonly IReviewService reviewService;
    private readonly IReviewSummaryService reviewSummaryService;

    /// <summary>
    /// Initializes the review controller with persistence and summary services.
    /// </summary>
    /// <param name="reviewService">The service used for creating and retrieving reviews.</param>
    /// <param name="reviewSummaryService">The service used for generating listing summaries.</param>
    public ReviewsController(
        IReviewService reviewService,
        IReviewSummaryService reviewSummaryService)
    {
        this.reviewService = reviewService;
        this.reviewSummaryService = reviewSummaryService;
    }

    /// <summary>
    /// Opens an authorized standalone editor as a progressive fallback for inline review panels.
    /// </summary>
    /// <param name="rentalRequestId">The renter's Completed rental.</param>
    /// <param name="returnUrl">The optional local return location.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The safe editor or a privacy-preserving not-found response.</returns>
    [Authorize]
    [HttpGet("{rentalRequestId:int}/edit")]
    public async Task<IActionResult> EditAsync(
        int rentalRequestId,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var editor = await reviewService.GetEditorAsync(rentalRequestId, User, cancellationToken);

        return editor is null ? NotFound() : View("Form", new ReviewFormViewModel
        {
            RentalRequestId = editor.RentalRequestId,
            ListingId = editor.ListingId,
            Rating = editor.Rating,
            Comment = editor.Comment,
            IsEditing = editor.Rating.HasValue,
            Open = true,
            ReturnUrl = LocalReturn(returnUrl)
        });
    }

    /// <summary>
    /// Creates a renter-authorized review with antiforgery protection and preserved validation input.
    /// </summary>
    /// <param name="submission">The explicitly editable review fields and optional listing context.</param>
    /// <param name="returnUrl">The optional local return location.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A safe JSON result, redirect or populated validation form.</returns>
    [Authorize]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CreateAsync(
        [FromForm] ReviewSubmission submission,
        [FromForm] string? returnUrl = null,
        CancellationToken cancellationToken = default) => SaveAsync(submission, returnUrl, false, cancellationToken);

    /// <summary>
    /// Updates only the authenticated renter's existing review.
    /// </summary>
    /// <param name="submission">The editable fields and optional listing context.</param>
    /// <param name="returnUrl">The optional local return location.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A safe JSON result, redirect or populated validation form.</returns>
    [Authorize]
    [HttpPost("update")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateAsync(
        [FromForm] ReviewSubmission submission,
        [FromForm] string? returnUrl = null,
        CancellationToken cancellationToken = default) => SaveAsync(submission, returnUrl, true, cancellationToken);

    /// <summary>
    /// Deletes only the renter's review; confirmations are collected in the UI and authorization remains server-side.
    /// </summary>
    /// <param name="rentalRequestId">The completed rental associated with the review.</param>
    /// <param name="returnUrl">The optional local return location.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A redirect or meaningful HTTP failure.</returns>
    [Authorize]
    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAsync(
        [FromForm] int rentalRequestId,
        [FromForm] string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var result = await reviewService.DeleteAsync(User, rentalRequestId, cancellationToken);

        if (result.IsSuccess)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
            {
                return NoContent();
            }

            TempData["RentalManagementFeedback"] = SharedResource.Get("Review.DeleteSucceeded");
            return LocalRedirect(LocalReturn(returnUrl));
        }

        return Failure(result, new ReviewSubmission(rentalRequestId, 0, null), returnUrl, true);
    }

    /// <summary>
    /// Exposes existing deterministic summary data only for published listings; this does not invoke AI.
    /// </summary>
    /// <param name="listingId">The public listing identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The public summary or an opaque not-found response.</returns>
    [AllowAnonymous]
    [HttpGet("summary")]
    public async Task<IActionResult> SummaryAsync([FromQuery] int listingId, CancellationToken cancellationToken = default)
    {
        if (listingId <= 0)
        {
            return BadRequest();
        }

        if (!await reviewService.IsPublishedAsync(listingId, cancellationToken))
        {
            return NotFound();
        }

        return Json(await reviewSummaryService.GetSummaryAsync(listingId, cancellationToken));
    }

    /// <summary>
    /// Delegates review persistence and keeps entity navigation graphs out of all JSON responses.
    /// </summary>
    /// <param name="submission">The submitted review fields.</param>
    /// <param name="returnUrl">The optional local return location.</param>
    /// <param name="editing">Whether to update an existing review.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The MVC result with safe review scalars only.</returns>
    private async Task<IActionResult> SaveAsync(
        ReviewSubmission submission,
        string? returnUrl,
        bool editing,
        CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? editing ? await reviewService.UpdateAsync(User, submission, cancellationToken)
                : await reviewService.CreateAsync(User, submission, cancellationToken)
            : new ReviewServiceResult(ReviewServiceStatus.ValidationFailed, null,
                ModelState.SelectMany(pair => pair.Value!.Errors.Select(error => new ReviewError(pair.Key, "Review.InvalidInput"))).ToArray());

        if (!result.IsSuccess || result.Value is null)
        {
            return Failure(result, submission, returnUrl, editing);
        }

        var message = SharedResource.Get(editing ? "Review.UpdateSucceeded" : "Review.SubmissionSucceeded");
        TempData["RentalManagementFeedback"] = message;
        TempData["ReviewFeedback"] = message;
        var destination = LocalReturn(returnUrl);
        var review = result.Value;

        if (WantsJson || string.IsNullOrWhiteSpace(returnUrl))
        {
            return Json(new
            {
                review = new { review.ReviewId, review.RentalRequestId, review.Rating, review.Comment, review.CreatedAtUtc },
                redirectUrl = destination
            });
        }

        return LocalRedirect(destination);
    }

    /// <summary>
    /// Returns terminal privacy failures or a populated form/structured JSON error list.
    /// </summary>
    /// <param name="result">The service outcome.</param>
    /// <param name="submission">The submitted values to retain.</param>
    /// <param name="returnUrl">The optional local return location.</param>
    /// <param name="editing">Whether the failed operation was an edit.</param>
    /// <returns>The failure response with meaningful localized text.</returns>
    private IActionResult Failure(
        ReviewServiceResult result,
        ReviewSubmission submission,
        string? returnUrl,
        bool editing)
    {
        var status = result.Status switch
        {
            ReviewServiceStatus.Unauthenticated => 401,
            ReviewServiceStatus.Forbidden => 403,
            ReviewServiceStatus.NotFound => 404,
            ReviewServiceStatus.ValidationFailed => 400,
            ReviewServiceStatus.Conflict or ReviewServiceStatus.ConcurrencyFailed => 409,
            _ => 503
        };

        if (WantsJson)
        {
            return StatusCode(status, new { errors = result.Errors.Select(error => new { error.Field, error.ResourceKey, error.Message }) });
        }

        if (status == 401)
        {
            return Challenge();
        }

        if (status == 403)
        {
            return Forbid();
        }

        if (status == 404)
        {
            return NotFound();
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Field, error.Message);
        }

        var view = View("Form", new ReviewFormViewModel
        {
            RentalRequestId = submission.RentalRequestId,
            ListingId = submission.ListingId,
            Rating = submission.Rating,
            Comment = submission.Comment,
            IsEditing = editing,
            Open = true,
            ReturnUrl = LocalReturn(returnUrl)
        });
        view.StatusCode = status;
        return view;
    }

    private bool WantsJson => Request.Headers.Accept.Any(value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>
    /// Restricts return navigation to local routes and defaults to the renter's history.
    /// </summary>
    /// <param name="returnUrl">The proposed return path.</param>
    /// <returns>A safe local path.</returns>
    private string LocalReturn(string? returnUrl) => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
        ? returnUrl : "/rentals/mine";
}
