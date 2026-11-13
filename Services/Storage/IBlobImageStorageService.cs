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
    /// <param name="content">The caller-owned stream containing the image bytes.</param>
    /// <param name="contentType">The image MIME content type.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the metadata describing the newly uploaded blob.</returns>
    /// <remarks>The caller retains ownership of the input stream, which need not be seekable.</remarks>
    Task<StoredBlobImage> UploadAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an authenticated image download whose content stream must be disposed by the caller.
    /// </summary>
    /// <param name="blobName">The blob name within the configured storage container.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the download result owning the authenticated image stream.</returns>
    /// <remarks>Network failures during subsequent stream reads propagate to the caller.</remarks>
    Task<BlobImageDownload> DownloadAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for a blob in the configured private container, returning false only when the blob is absent.
    /// </summary>
    /// <param name="blobName">The blob name within the configured storage container.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning whether the blob exists in the configured container.</returns>
    Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob if present, returning false if it has already been removed.
    /// </summary>
    /// <param name="blobName">The blob name within the configured storage container.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning whether an existing blob was deleted.</returns>
    Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default);
}
