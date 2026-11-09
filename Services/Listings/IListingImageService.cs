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
    Task<ListingServiceResult<ListingImage>> UploadAsync(int listingId, ClaimsPrincipal? user, Stream? content, string? contentType,
        string? originalFileName = null, string? altText = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes selected metadata without invalidating publication, then attempts idempotent blob cleanup.
    /// </summary>
    Task<ListingServiceResult<ListingImageDeletion>> DeleteAsync(int listingId, int imageId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects exactly one of this listing's images as primary using an atomic database transaction.
    /// </summary>
    Task<ListingServiceResult<ListingImage>> SetPrimaryAsync(int listingId, int imageId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets zero-based display positions from a complete permutation of this listing's current image IDs.
    /// </summary>
    Task<ListingServiceResult<IReadOnlyList<ListingImage>>> SetDisplayOrderAsync(int listingId, IReadOnlyList<int>? imageIds,
        ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves ordered image metadata, including original filenames, only for the authenticated listing owner.
    /// </summary>
    Task<ListingServiceResult<IReadOnlyList<ListingImage>>> GetImagesAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a private-storage image for a published listing or its authenticated owner, resolving storage names server-side.
    /// </summary>
    /// <remarks>The successful result owns a stream that the caller must dispose after delivery.</remarks>
    Task<ListingServiceResult<BlobImageDownload>> DownloadAsync(int listingId, int imageId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Distinguishes committed metadata removal from completed physical blob deletion.
/// </summary>
public sealed record ListingImageDeletion(int ListingImageId, bool MetadataRemoved, bool BlobRemoved);
