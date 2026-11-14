using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using RentRoad.Data;
using RentRoad.Models;
using RentRoad.Models.Enums;

namespace RentRoad.Services.Reviews;

/// <summary>
/// Creates renter reviews for completed rentals and provides aggregate review summaries.
/// </summary>
public sealed class ReviewService : IReviewService
{
    private readonly ApplicationDbContext context;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ReviewService> logger;

    /// <summary>
    /// Initializes the review service with its scoped database context, UTC clock, and logger.
    /// </summary>
    /// <param name="context">The application database context used for review persistence.</param>
    /// <param name="timeProvider">The clock used to stamp created reviews in UTC.</param>
    /// <param name="logger">The logger used when database operations fail.</param>
    public ReviewService(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<ReviewService> logger)
    {
        this.context = context;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task<ReviewServiceResult> CreateAsync(
        ClaimsPrincipal? user,
        ReviewSubmission? submission,
        CancellationToken cancellationToken = default) =>
        SubmitAsync(user, submission, cancellationToken);

    /// <inheritdoc />
    public async Task<ReviewServiceResult> UpdateAsync(
        ClaimsPrincipal? user,
        ReviewSubmission? submission,
        CancellationToken cancellationToken = default)
    {
        var authorization = await GetOwnedCompletedRequestAsync(user, submission?.RentalRequestId ?? 0, cancellationToken);
        if (!authorization.IsSuccess)
        {
            return authorization.Result!;
        }

        if (submission is null)
        {
            return ReviewServiceResult.Failure(
                ReviewServiceStatus.ValidationFailed,
                new ReviewError(string.Empty, "Review.DetailsRequired"));
        }

        var validationErrors = ValidateSubmission(submission);
        if (validationErrors.Count > 0)
        {
            return ReviewServiceResult.Failure(ReviewServiceStatus.ValidationFailed, validationErrors.ToArray());
        }

        authorization.Request!.Review!.Rating = submission.Rating;
        authorization.Request.Review.Comment = string.IsNullOrWhiteSpace(submission.Comment) ? null : submission.Comment.Trim();

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return ReviewServiceResult.Success(authorization.Request.Review);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or RetryLimitExceededException)
        {
            logger.LogError(exception, "Review update failed for rental request {RentalRequestId}.", submission.RentalRequestId);
            return ReviewServiceResult.Failure(ReviewServiceStatus.PersistenceFailed,
                new ReviewError(nameof(submission.RentalRequestId), "Review.PersistenceFailed"));
        }
    }

    /// <inheritdoc />
    public async Task<ReviewServiceResult> DeleteAsync(
        ClaimsPrincipal? user,
        int rentalRequestId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await GetOwnedCompletedRequestAsync(user, rentalRequestId, cancellationToken);
        if (!authorization.IsSuccess)
        {
            return authorization.Result!;
        }

        context.Reviews.Remove(authorization.Request!.Review!);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new ReviewServiceResult(ReviewServiceStatus.Success, null, Array.Empty<ReviewError>());
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or RetryLimitExceededException)
        {
            logger.LogError(exception, "Review deletion failed for rental request {RentalRequestId}.", rentalRequestId);
            return ReviewServiceResult.Failure(ReviewServiceStatus.PersistenceFailed,
                new ReviewError(nameof(rentalRequestId), "Review.PersistenceFailed"));
        }
    }

    /// <inheritdoc />
    public async Task<ReviewServiceResult> SubmitAsync(
        ClaimsPrincipal? user,
        ReviewSubmission? submission,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userId = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return ReviewServiceResult.Failure(
                ReviewServiceStatus.Unauthenticated,
                new ReviewError(nameof(ReviewSubmission.RentalRequestId), "Review.SignInRequired"));
        }

        if (submission is null)
        {
            return ReviewServiceResult.Failure(
                ReviewServiceStatus.ValidationFailed,
                new ReviewError(string.Empty, "Review.DetailsRequired"));
        }

        var validationErrors = ValidateSubmission(submission);
        if (validationErrors.Count > 0)
        {
            return ReviewServiceResult.Failure(ReviewServiceStatus.ValidationFailed, validationErrors.ToArray());
        }

        try
        {
            var request = await context.RentalRequests
                .Include(request => request.Review)
                .SingleOrDefaultAsync(request => request.RentalRequestId == submission.RentalRequestId, cancellationToken);

            if (request is null)
            {
                return ReviewServiceResult.Failure(
                    ReviewServiceStatus.NotFound,
                    new ReviewError(nameof(submission.RentalRequestId), "Review.RentalRequestNotFound"));
            }

            if (!string.Equals(request.RenterId, userId, StringComparison.Ordinal))
            {
                return ReviewServiceResult.Failure(
                    ReviewServiceStatus.Forbidden,
                    new ReviewError(nameof(Review.RentalRequestId), "Review.NotYourRental"));
            }

            if (request.Status != RentalStatus.Completed)
            {
                return ReviewServiceResult.Failure(
                    ReviewServiceStatus.Conflict,
                    new ReviewError(nameof(Review.RentalRequestId), "Review.RentalMustBeCompleted"));
            }

            if (request.Review is not null)
            {
                return ReviewServiceResult.Failure(
                    ReviewServiceStatus.Conflict,
                    new ReviewError(nameof(submission.RentalRequestId), "Review.AlreadyExists"));
            }

            var review = new Review
            {
                RentalRequestId = request.RentalRequestId,
                Rating = submission.Rating,
                Comment = string.IsNullOrWhiteSpace(submission.Comment) ? null : submission.Comment.Trim(),
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            };

            context.Reviews.Add(review);
            await context.SaveChangesAsync(cancellationToken);

            return ReviewServiceResult.Success(review);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or RetryLimitExceededException)
        {
            logger.LogError(
                exception,
                "Review persistence failed for rental request {RentalRequestId}.",
                submission.RentalRequestId);

            return ReviewServiceResult.Failure(
                ReviewServiceStatus.PersistenceFailed,
                new ReviewError(nameof(submission.RentalRequestId), "Review.PersistenceFailed"));
        }
    }

    /// <inheritdoc />
    public async Task<bool> CanReviewAsync(
        int rentalRequestId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        if (rentalRequestId <= 0)
        {
            return false;
        }

        var userId = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var request = await context.RentalRequests
            .Include(request => request.Review)
            .SingleOrDefaultAsync(request => request.RentalRequestId == rentalRequestId, cancellationToken);

        if (request is null || !string.Equals(request.RenterId, userId, StringComparison.Ordinal))
        {
            return false;
        }

        return request.Status == RentalStatus.Completed && request.Review is null;
    }

    /// <inheritdoc />
    public Task<Review?> GetByRentalRequestAsync(
        int rentalRequestId,
        CancellationToken cancellationToken = default) =>
        context.Reviews
            .Include(review => review.RentalRequest)
            .SingleOrDefaultAsync(review => review.RentalRequestId == rentalRequestId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Review>> GetForListingAsync(
        int listingId,
        CancellationToken cancellationToken = default)
    {
        if (listingId <= 0)
        {
            return Array.Empty<Review>();
        }

        return await context.Reviews
            .Include(review => review.RentalRequest)
            .Where(review => review.RentalRequest.ListingId == listingId
                && review.RentalRequest.Status == RentalStatus.Completed)
            .OrderBy(review => review.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReviewSummary> GetSummaryAsync(
        int listingId,
        CancellationToken cancellationToken = default)
    {
        var reviews = await GetForListingAsync(listingId, cancellationToken);
        if (reviews.Count == 0)
        {
            return new ReviewSummary(
                listingId,
                0,
                0,
                "No reviews yet for this listing.",
                timeProvider.GetUtcNow().UtcDateTime);
        }

        var average = reviews.Average(review => review.Rating);
        var comments = reviews
            .Where(review => !string.IsNullOrWhiteSpace(review.Comment))
            .Select(review => review.Comment!.Trim())
            .Where(comment => comment.Length > 0)
            .Take(3)
            .ToArray();

        var sentiment = average >= 4.5 ? "consistently excellent" : average >= 3.5 ? "strongly positive" : average >= 2.5 ? "mixed but generally workable" : "challenging";
        var summaryText = comments.Length > 0
            ? $"Renters described this listing as {sentiment}, with feedback including \"{string.Join("\"; \"", comments)}\"."
            : $"Renters described this listing as {sentiment} overall.";

        return new ReviewSummary(
            listingId,
            Math.Round(average, 2),
            reviews.Count,
            summaryText,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// Validates the rating and comment payload before persisting a review.
    /// </summary>
    /// <param name="submission">The submitted review content.</param>
    /// <returns>A list of review validation errors, if any.</returns>
    private static List<ReviewError> ValidateSubmission(ReviewSubmission submission)
    {
        var errors = new List<ReviewError>();

        if (submission.RentalRequestId <= 0)
        {
            errors.Add(new ReviewError(nameof(submission.RentalRequestId), "Review.InvalidRentalRequestId"));
        }

        if (submission.Rating is < 1 or > 5)
        {
            errors.Add(new ReviewError(nameof(submission.Rating), "Review.InvalidRating"));
        }

        if (!string.IsNullOrWhiteSpace(submission.Comment) && submission.Comment.Trim().Length > 2000)
        {
            errors.Add(new ReviewError(nameof(submission.Comment), "Review.CommentTooLong"));
        }

        return errors;
    }

    /// <summary>
    /// Loads and authorizes a completed rental request with its existing review.
    /// </summary>
    /// <param name="user">The authenticated renter.</param>
    /// <param name="rentalRequestId">The rental request identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The authorized request or a structured failure.</returns>
    private async Task<ReviewAuthorization> GetOwnedCompletedRequestAsync(
        ClaimsPrincipal? user,
        int rentalRequestId,
        CancellationToken cancellationToken)
    {
        var userId = user?.Identities.FirstOrDefault(identity => identity.IsAuthenticated)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ReviewAuthorization.Failed(ReviewServiceResult.Failure(ReviewServiceStatus.Unauthenticated,
                new ReviewError(nameof(ReviewSubmission.RentalRequestId), "Review.SignInRequired")));
        }

        if (rentalRequestId <= 0)
        {
            return ReviewAuthorization.Failed(ReviewServiceResult.Failure(ReviewServiceStatus.ValidationFailed,
                new ReviewError(nameof(ReviewSubmission.RentalRequestId), "Review.InvalidRentalRequestId")));
        }

        var request = await context.RentalRequests.Include(item => item.Review)
            .SingleOrDefaultAsync(item => item.RentalRequestId == rentalRequestId, cancellationToken);

        if (request is null)
        {
            return ReviewAuthorization.Failed(ReviewServiceResult.Failure(ReviewServiceStatus.NotFound,
                new ReviewError(nameof(ReviewSubmission.RentalRequestId), "Review.RentalRequestNotFound")));
        }

        if (!string.Equals(request.RenterId, userId, StringComparison.Ordinal))
        {
            return ReviewAuthorization.Failed(ReviewServiceResult.Failure(ReviewServiceStatus.Forbidden,
                new ReviewError(nameof(ReviewSubmission.RentalRequestId), "Review.NotYourRental")));
        }

        if (request.Status != RentalStatus.Completed || request.Review is null)
        {
            return ReviewAuthorization.Failed(ReviewServiceResult.Failure(ReviewServiceStatus.Conflict,
                new ReviewError(nameof(ReviewSubmission.RentalRequestId), "Review.RentalMustBeCompleted")));
        }

        return ReviewAuthorization.Succeeded(request);
    }

    private sealed record ReviewAuthorization(RentalRequest? Request, ReviewServiceResult? Result)
    {
        public bool IsSuccess => Request is not null;

        public static ReviewAuthorization Succeeded(RentalRequest request) => new(request, null);

        public static ReviewAuthorization Failed(ReviewServiceResult result) => new(null, result);
    }
}
