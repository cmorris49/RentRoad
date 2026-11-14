using System.Security.Claims;
using RentRoad.Models;

namespace RentRoad.Services.Reviews;

/// <summary>
/// Manages renter reviews for completed rentals and exposes listing-level review aggregates.
/// </summary>
public interface IReviewService
{
    /// <summary>
    /// Stores a renter review for a completed rental request when authorization and business rules allow it.
    /// </summary>
    /// <param name="user">The authenticated user submitting the review.</param>
    /// <param name="submission">The details of the review to create.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The review result containing either the saved review or structured validation errors.</returns>
    Task<ReviewServiceResult> CreateAsync(
        ClaimsPrincipal? user,
        ReviewSubmission? submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Alias for creating a review, kept for callers that prefer a submit-style API name.
    /// </summary>
    /// <param name="user">The authenticated user submitting the review.</param>
    /// <param name="submission">The details of the review to create.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The review result containing either the saved review or structured validation errors.</returns>
    Task<ReviewServiceResult> SubmitAsync(
        ClaimsPrincipal? user,
        ReviewSubmission? submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a review while its originating rental remains completed and owned by the renter.
    /// </summary>
    /// <param name="user">The authenticated renter updating the review.</param>
    /// <param name="submission">The updated rating and comment.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The review result containing the updated review or structured errors.</returns>
    Task<ReviewServiceResult> UpdateAsync(
        ClaimsPrincipal? user,
        ReviewSubmission? submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a review while its originating rental remains completed and owned by the renter.
    /// </summary>
    /// <param name="user">The authenticated renter deleting the review.</param>
    /// <param name="rentalRequestId">The completed rental request associated with the review.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The operation result containing any structured errors.</returns>
    Task<ReviewServiceResult> DeleteAsync(
        ClaimsPrincipal? user,
        int rentalRequestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether the authenticated renter is allowed to review the specified rental request.
    /// </summary>
    /// <param name="rentalRequestId">The rental request identifier to evaluate.</param>
    /// <param name="user">The authenticated user requesting eligibility.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>True when a review may be created; otherwise false.</returns>
    Task<bool> CanReviewAsync(
        int rentalRequestId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the single review created for the specified rental request, if it exists.
    /// </summary>
    /// <param name="rentalRequestId">The rental request identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The matching review, when one exists.</returns>
    Task<Review?> GetByRentalRequestAsync(
        int rentalRequestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all reviews for a listing in creation order.
    /// </summary>
    /// <param name="listingId">The listing identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The review entries associated with the listing.</returns>
    Task<IReadOnlyList<Review>> GetForListingAsync(
        int listingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a summary of ratings and sentiment for a listing's completed reviews.
    /// </summary>
    /// <param name="listingId">The listing identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The aggregate review summary for the listing.</returns>
    Task<ReviewSummary> GetSummaryAsync(
        int listingId,
        CancellationToken cancellationToken = default);
}
