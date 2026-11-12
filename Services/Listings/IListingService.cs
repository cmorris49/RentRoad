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
/// Availability writes, bulk window replacement, and publication use independent transactions and return detached entities.
/// Save or discard pending listing/availability changes and finish caller transactions before invoking these operations.
/// </remarks>
public interface IListingService
{
    /// <summary>Checks ownership for presentation using the authenticated principal without exposing Identity IDs or loading an entity graph.</summary>
    Task<bool> IsOwnedByAsync(int listingId, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>Checks proposed scalar fields against existing publication rules without saving or publishing them.</summary>
    Task<ListingServiceResult<bool>> ValidateProposedForPublicationAsync(int listingId, ClaimsPrincipal? user,
        ListingUpdate details, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves one owner's listing with ordered windows and private blocks for availability management.
    /// </summary>
    Task<ListingServiceResult<Listing>> GetOwnerAvailabilityAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces only availability windows through existing transactional validation, preserving listing scalar details.
    /// </summary>
    Task<ListingServiceResult<Listing>> ReplaceAvailabilityWindowsAsync(int listingId, ClaimsPrincipal? user,
        IReadOnlyList<ListingAvailabilityPeriod>? windows, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an owner's availability window without invalidating approved or active reservations.
    /// </summary>
    Task<ListingServiceResult<AvailabilityWindow>> CreateAvailabilityWindowAsync(int listingId, ClaimsPrincipal? user,
        ListingAvailabilityPeriod? period, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an owner's availability window while preserving reservation coverage.
    /// </summary>
    Task<ListingServiceResult<AvailabilityWindow>> UpdateAvailabilityWindowAsync(int listingId, int windowId,
        ClaimsPrincipal? user, ListingAvailabilityPeriod? period, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a window only if remaining windows preserve reservations and publication requirements.
    /// </summary>
    Task<ListingServiceResult<AvailabilityWindow>> RemoveAvailabilityWindowAsync(int listingId, int windowId,
        ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an owner's unavailable period without overlapping approved or active reservations.
    /// </summary>
    Task<ListingServiceResult<AvailabilityBlock>> CreateAvailabilityBlockAsync(int listingId, ClaimsPrincipal? user,
        ListingAvailabilityBlockUpdate? details, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an owner's unavailable period and private reason while protecting reservations.
    /// </summary>
    Task<ListingServiceResult<AvailabilityBlock>> UpdateAvailabilityBlockAsync(int listingId, int blockId,
        ClaimsPrincipal? user, ListingAvailabilityBlockUpdate? details, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an owner's unavailable period without deleting listing or rental history.
    /// </summary>
    Task<ListingServiceResult<AvailabilityBlock>> RemoveAvailabilityBlockAsync(int listingId, int blockId,
        ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches published listings or browses them with default filters and pagination when parameters are omitted.
    /// </summary>
    /// <returns>A result page, including an empty page for no matches, or field-specific validation errors.</returns>
    Task<ListingServiceResult<ListingSearchPage>> SearchPublishedAsync(ListingSearchParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects up to ten newest published cards as the temporary Home popularity selection.
    /// </summary>
    Task<IReadOnlyList<ListingSummary>> GetPopularListingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects up to ten newest public cards in a published listing's category, excluding that listing.
    /// </summary>
    Task<IReadOnlyList<ListingSummary>> GetSimilarListingsAsync(int listingId, CancellationToken cancellationToken = default);

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
    /// Validates supplied draft details and creates the owned draft in one save, allowing incomplete fields.
    /// </summary>
    Task<ListingServiceResult<Listing>> CreateDraftAsync(ClaimsPrincipal? user, ListingUpdate? details,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves one detached listing after verifying the authenticated owner, including drafts and archived listings.
    /// </summary>
    Task<ListingServiceResult<Listing>> GetOwnerListingAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

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
