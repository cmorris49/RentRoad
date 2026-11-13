using System.Security.Claims;
using RentRoad.Models;
using RentRoad.Services.Storage;

namespace RentRoad.Services.Listings;

/// <summary>
/// Associates private stored images with owned listings without binding submitted blob or owner identifiers.
/// </summary>
/// <remarks>
/// Callers pass the trusted Identity principal. Returned entities are detached service data, not MVC form models.
/// Save pending listing changes and finish caller transactions before mutations. Archived listings are read-only.
/// CleanupPending on deletion includes a value confirming metadata removal; it must not be reported as a complete rollback.
/// </remarks>
public interface IListingImageService
{
    /// <summary>
    /// Uploads validated bytes and persists their server-generated storage metadata, compensating failed SQL writes.
    /// </summary>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="content">The caller-owned stream containing the image bytes.</param>
    /// <param name="contentType">The image MIME content type.</param>
    /// <param name="originalFileName">The optional original filename retained as image metadata.</param>
    /// <param name="altText">The optional alternative text for the listing image.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<ListingServiceResult<ListingImage>> UploadAsync(
        int listingId,
        ClaimsPrincipal? user,
        Stream? content,
        string? contentType,
        string? originalFileName = null,
        string? altText = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes selected metadata without invalidating publication, then attempts idempotent blob cleanup.
    /// </summary>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="imageId">The identifier of the selected listing image, when supplied.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<ListingServiceResult<ListingImageDeletion>> DeleteAsync(
        int listingId,
        int imageId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects exactly one of this listing's images as primary using an atomic database transaction.
    /// </summary>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="imageId">The identifier of the selected listing image, when supplied.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<ListingServiceResult<ListingImage>> SetPrimaryAsync(
        int listingId,
        int imageId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets zero-based display positions from a complete permutation of this listing's current image IDs.
    /// </summary>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="imageIds">The complete requested sequence of listing image identifiers.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<ListingServiceResult<IReadOnlyList<ListingImage>>> SetDisplayOrderAsync(
        int listingId,
        IReadOnlyList<int>? imageIds,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves ordered image metadata, including original filenames, only for the authenticated listing owner.
    /// </summary>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    Task<ListingServiceResult<IReadOnlyList<ListingImage>>> GetImagesAsync(
        int listingId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a private-storage image for a published listing or its authenticated owner, resolving storage names
    /// server-side.
    /// </summary>
    /// <param name="listingId">The identifier of the listing being processed.</param>
    /// <param name="imageId">The identifier of the selected listing image, when supplied.</param>
    /// <param name="user">The Identity principal supplied by the application boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the operation result containing the returned value or structured errors.</returns>
    /// <remarks>The successful result owns a stream that the caller must dispose after delivery.</remarks>
    Task<ListingServiceResult<BlobImageDownload>> DownloadAsync(
        int listingId,
        int imageId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Distinguishes committed metadata removal from completed physical blob deletion.
/// </summary>
/// <param name="ListingImageId">The identifier of the listing image.</param>
/// <param name="MetadataRemoved">Whether image metadata removal was confirmed.</param>
/// <param name="BlobRemoved">Whether physical blob removal was confirmed.</param>
public sealed record ListingImageDeletion(int ListingImageId, bool MetadataRemoved, bool BlobRemoved);
