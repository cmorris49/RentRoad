using System.Security.Claims;
using RentRoad.Models;

namespace RentRoad.Services.Listings;

/// <summary>
/// Manages owner listings and provides published listing data for public browsing.
/// </summary>
/// <remarks>
/// Management callers supply the authenticated principal from Identity, never a principal or owner ID bound from form data.
/// Public search and details require no principal and return projections that exclude private account data.
/// Expected validation, authorization, and missing-record failures are returned as structured results.
/// Cancellation and unexpected persistence failures propagate to the application's error handling.
/// </remarks>
public interface IListingService
{
    /// <summary>
    /// Searches published listings or browses them with default filters and pagination when parameters are omitted.
    /// </summary>
    /// <returns>A result page, including an empty page for no matches, or field-specific validation errors.</returns>
    Task<ListingServiceResult<ListingSearchPage>> SearchPublishedAsync(ListingSearchParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves public listing details, returning NotFound for missing or unpublished listings.
    /// </summary>
    Task<ListingServiceResult<ListingDetails>> GetPublishedDetailsAsync(int listingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an incomplete draft owned by the authenticated user.
    /// </summary>
    Task<ListingServiceResult<Listing>> CreateDraftAsync(ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates editable details while preserving ownership, creation time, and rental history.
    /// </summary>
    Task<ListingServiceResult<Listing>> UpdateAsync(int listingId, ClaimsPrincipal? user, ListingUpdate? details,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks all publication requirements without saving changes.
    /// </summary>
    Task<ListingServiceResult<Listing>> ValidateForPublicationAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a completed listing and records its first publication time.
    /// </summary>
    Task<ListingServiceResult<Listing>> PublishAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives a listing without deleting it or its related records.
    /// </summary>
    Task<ListingServiceResult<Listing>> ArchiveAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all listings owned by the authenticated user, including drafts and archived listings.
    /// </summary>
    Task<ListingServiceResult<IReadOnlyList<Listing>>> GetOwnerListingsAsync(ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);
}
