using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Services.Reviews;

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
    /// Submits a review for a completed rental request using the authenticated identity.
    /// </summary>
    /// <param name="submission">The submitted review data.</param>
    /// <param name="returnUrl">The optional local history URL to return to after a browser form submission.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A JSON result for successful review creation or a validation problem otherwise.</returns>
    [Authorize]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAsync(
        [FromForm] ReviewSubmission submission,
        [FromForm] string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await reviewService.CreateAsync(User, submission, cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                TempData["RentalManagementFeedback"] = SharedResource.Get("Review.SubmissionSucceeded");
                return LocalRedirect(returnUrl);
            }

            return Json(new { review = result.Value });
        }

        return result.Status switch
        {
            ReviewServiceStatus.Unauthenticated => Challenge(),
            ReviewServiceStatus.Forbidden => Forbid(),
            ReviewServiceStatus.NotFound => NotFound(),
            ReviewServiceStatus.ValidationFailed => BadRequest(result.Errors),
            ReviewServiceStatus.Conflict => Conflict(result.Errors),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, result.Errors)
        };
    }

    /// <summary>
    /// Updates a renter's review and redirects back to the supplied local history URL.
    /// </summary>
    /// <param name="submission">The updated review data.</param>
    /// <param name="returnUrl">The optional local history URL to return to.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A redirect for browser forms or a structured operation failure.</returns>
    [Authorize]
    [HttpPost("update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAsync(
        [FromForm] ReviewSubmission submission,
        [FromForm] string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await reviewService.UpdateAsync(User, submission, cancellationToken);
        if (result.Status == ReviewServiceStatus.Success && IsLocalReturnUrl(returnUrl))
        {
            TempData["RentalManagementFeedback"] = SharedResource.Get("Review.UpdateSucceeded");
            return LocalRedirect(returnUrl!);
        }

        if (result.Status == ReviewServiceStatus.Success && result.Value is not null)
        {
            return Json(new { review = result.Value });
        }

        return ReviewFailure(result);
    }

    /// <summary>
    /// Deletes a renter's review and redirects back to the supplied local history URL.
    /// </summary>
    /// <param name="rentalRequestId">The completed rental request associated with the review.</param>
    /// <param name="returnUrl">The optional local history URL to return to.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A redirect for browser forms or a structured operation failure.</returns>
    [Authorize]
    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAsync(
        [FromForm] int rentalRequestId,
        [FromForm] string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var result = await reviewService.DeleteAsync(User, rentalRequestId, cancellationToken);
        if (result.Status == ReviewServiceStatus.Success && IsLocalReturnUrl(returnUrl))
        {
            TempData["RentalManagementFeedback"] = SharedResource.Get("Review.DeleteSucceeded");
            return LocalRedirect(returnUrl!);
        }

        if (result.Status == ReviewServiceStatus.Success)
        {
            return NoContent();
        }

        return ReviewFailure(result);
    }

    /// <summary>
    /// Returns the aggregated review summary for the requested listing.
    /// </summary>
    /// <param name="listingId">The listing identifier to summarize.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The listing summary as JSON.</returns>
    [AllowAnonymous]
    [HttpGet("summary")]
    public async Task<IActionResult> SummaryAsync(
        [FromQuery] int listingId,
        CancellationToken cancellationToken = default)
    {
        if (listingId <= 0)
        {
            return BadRequest();
        }

        var summary = await reviewSummaryService.GetSummaryAsync(listingId, cancellationToken);
        return Json(summary);
    }

    /// <summary>
    /// Determines whether the supplied return path stays within the application.
    /// </summary>
    /// <param name="returnUrl">The path supplied by the browser form.</param>
    /// <returns>True when the path is local.</returns>
    private bool IsLocalReturnUrl(string? returnUrl) => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl);

    /// <summary>
    /// Converts a review service failure into an HTTP response.
    /// </summary>
    /// <param name="result">The review operation result.</param>
    /// <returns>The corresponding MVC response.</returns>
    private IActionResult ReviewFailure(ReviewServiceResult result) => result.Status switch
    {
        ReviewServiceStatus.Unauthenticated => Challenge(),
        ReviewServiceStatus.Forbidden => Forbid(),
        ReviewServiceStatus.NotFound => NotFound(),
        ReviewServiceStatus.ValidationFailed => BadRequest(result.Errors),
        ReviewServiceStatus.Conflict => Conflict(result.Errors),
        _ => StatusCode(StatusCodes.Status503ServiceUnavailable, result.Errors)
    };
}
