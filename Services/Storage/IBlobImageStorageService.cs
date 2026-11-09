namespace RentRoad.Services.Storage;

/// <summary>
/// Stores listing image bytes in a configured private Azure container, independently of database records.
/// </summary>
/// <remarks>
/// Callers authorize listing access and resolve blob names from trusted metadata before calling this service.
/// No method grants public access or returns credentials, blob URLs, or SAS tokens.
/// Invalid input raises ArgumentException; Azure failures raise BlobImageStorageException; cancellation propagates.
/// </remarks>
public interface IBlobImageStorageService
{
    /// <summary>
    /// Validates and uploads the remaining stream bytes under a new name without overwriting existing blobs.
    /// </summary>
    /// <remarks>The caller retains ownership of the input stream, which need not be seekable.</remarks>
    Task<StoredBlobImage> UploadAsync(Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an authenticated image download whose content stream must be disposed by the caller.
    /// </summary>
    /// <remarks>Network failures during subsequent stream reads propagate to the caller.</remarks>
    Task<BlobImageDownload> DownloadAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for a blob in the configured private container, returning false only when the blob is absent.
    /// </summary>
    Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob if present, returning false if it has already been removed.
    /// </summary>
    Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default);
}
