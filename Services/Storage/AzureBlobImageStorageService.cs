using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace RentRoad.Services.Storage;

/// <summary>
/// Validates listing image uploads and accesses an existing private Azure Blob Storage container.
/// </summary>
public sealed class AzureBlobImageStorageService : IBlobImageStorageService
{
    private const int COPY_BUFFER_SIZE = 81920;
    private readonly BlobContainerClient container;
    private readonly int maxImageSizeBytes;

    /// <summary>
    /// Initializes storage with an injectable Azure client and the configured image byte limit.
    /// </summary>
    /// <param name="container">The Azure client for the configured image storage container.</param>
    /// <param name="options">The image storage configuration and validation limits.</param>
    public AzureBlobImageStorageService(
        BlobContainerClient container,
        IOptions<BlobImageStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(options);

        if (container.Uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Listing image storage requires an HTTPS Azure endpoint.");
        }

        maxImageSizeBytes = options.Value.MaxImageSizeBytes;

        if (maxImageSizeBytes is < 1 or > BlobImageStorageOptions.MAX_ALLOWED_IMAGE_SIZE_BYTES)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The image size limit must be between 1 byte and 20 MiB.");
        }

        this.container = container;
    }

    /// <summary>
    /// Uploads validated JPEG, PNG, or WebP bytes using a unique name and an atomic no-overwrite condition.
    /// </summary>
    /// <param name="content">The caller-owned stream containing the image bytes.</param>
    /// <param name="contentType">The image MIME content type.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the metadata describing the newly uploaded blob.</returns>
    public async Task<StoredBlobImage> UploadAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanRead)
        {
            throw new ArgumentException("The image stream must be readable.", nameof(content));
        }

        var normalizedType = NormalizeContentType(contentType);
        await using var validated = await BufferImageAsync(content, cancellationToken);

        if (!MatchesSignature(validated.GetBuffer().AsSpan(0, (int)validated.Length), normalizedType))
        {
            throw new ArgumentException(
                "Image bytes must match the supplied JPEG, PNG, or WebP content type.",
                nameof(content));
        }

        var extension = normalizedType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => ".webp"
        };
        var blobName = $"images/{Guid.NewGuid():N}{extension}";

        return await ExecuteStorageAsync(async () =>
            {
                await EnsurePrivateContainerAsync(cancellationToken);
                await container.GetBlobClient(blobName).UploadAsync(
                    validated,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = normalizedType },
                        Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                    },
                    cancellationToken);
                return new StoredBlobImage(container.Name, blobName, normalizedType, validated.Length);
            });
    }

    /// <summary>
    /// Downloads through the authenticated SDK and rejects unsupported or oversized stored image metadata.
    /// </summary>
    /// <param name="blobName">The blob name within the configured storage container.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the download result owning the authenticated image stream.</returns>
    public Task<BlobImageDownload> DownloadAsync(
        string blobName,
        CancellationToken cancellationToken = default)
    {
        ValidateBlobName(blobName);
        return ExecuteStorageAsync(async () =>
            {
                await EnsurePrivateContainerAsync(cancellationToken);
                var response = await container.GetBlobClient(blobName)
                    .DownloadStreamingAsync(cancellationToken: cancellationToken);
                var downloaded = response.Value;
                var type = downloaded.Details.ContentType?.Trim().ToLowerInvariant();

                if (!IsSupportedContentType(type) || downloaded.Details.ContentLength <= 0
                    || downloaded.Details.ContentLength > maxImageSizeBytes)
                {
                    await downloaded.Content.DisposeAsync();
                    throw new BlobImageStorageException(
                        BlobImageStorageFailure.InvalidStoredImage,
                        "The stored blob does not have supported image metadata or exceeds the configured size limit.");
                }

                return new BlobImageDownload(downloaded.Content, type!, downloaded.Details.ContentLength);
            });
    }

    /// <summary>
    /// Checks blob existence without treating storage authorization or connectivity failures as missing images.
    /// </summary>
    /// <param name="blobName">The blob name within the configured storage container.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning whether the blob exists in the configured container.</returns>
    public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken = default)
    {
        ValidateBlobName(blobName);
        return ExecuteStorageAsync(async () =>
            {
                await EnsurePrivateContainerAsync(cancellationToken);
                return (await container.GetBlobClient(blobName).ExistsAsync(cancellationToken)).Value;
            });
    }

    /// <summary>
    /// Deletes only the selected blob, returning false for an absent blob without changing container resources.
    /// </summary>
    /// <param name="blobName">The blob name within the configured storage container.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning whether an existing blob was deleted.</returns>
    public Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        ValidateBlobName(blobName);
        return ExecuteStorageAsync(async () =>
            {
                await EnsurePrivateContainerAsync(cancellationToken);
                return (await container.GetBlobClient(blobName)
                    .DeleteIfExistsAsync(cancellationToken: cancellationToken)).Value;
            });
    }

    /// <summary>
    /// Refuses public containers without creating resources or changing their access policy.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task representing completion of the container privacy check.</returns>
    private async Task EnsurePrivateContainerAsync(CancellationToken cancellationToken)
    {
        var properties = await container.GetPropertiesAsync(cancellationToken: cancellationToken);

        if (properties.Value.PublicAccess is { } access && access != PublicAccessType.None)
        {
            throw new BlobImageStorageException(
                BlobImageStorageFailure.ContainerNotPrivate,
                "Listing images require a private storage container. Disable anonymous access before using this service.");
        }
    }

    /// <summary>
    /// Counts actual bytes while buffering a bounded upload, leaving the caller's input stream open.
    /// </summary>
    /// <param name="content">The caller-owned stream containing the image bytes.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task returning the bounded image buffer positioned for reading.</returns>
    private async Task<MemoryStream> BufferImageAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffered = new MemoryStream();

        try
        {
            var buffer = new byte[Math.Min(COPY_BUFFER_SIZE, maxImageSizeBytes + 1)];
            int read;

            while ((read = await content.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
            {
                if (buffered.Length + read > maxImageSizeBytes)
                {
                    throw new ArgumentException("The image exceeds the configured size limit.", nameof(content));
                }

                await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (buffered.Length == 0)
            {
                throw new ArgumentException("The image must not be empty.", nameof(content));
            }

            buffered.Position = 0;
            return buffered;
        }
        catch
        {
            await buffered.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Matches format signatures rather than trusting a submitted filename or MIME type alone.
    /// </summary>
    /// <param name="bytes">The image bytes whose file signature is checked.</param>
    /// <param name="contentType">The image MIME content type.</param>
    /// <returns>Whether the bytes match the declared supported image format.</returns>
    private static bool MatchesSignature(ReadOnlySpan<byte> bytes, string contentType) =>
        contentType switch
        {
            "image/jpeg" => bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
            "image/png" => bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            "image/webp" => bytes.Length >= 16 && bytes[..4].SequenceEqual("RIFF"u8)
                && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
                && (bytes.Slice(12, 4).SequenceEqual("VP8 "u8) || bytes.Slice(12, 4).SequenceEqual("VP8L"u8)
                || bytes.Slice(12, 4).SequenceEqual("VP8X"u8)),
            _ => false
        };

    /// <summary>
    /// Validates and canonicalizes the supported MIME types.
    /// </summary>
    /// <param name="contentType">The image MIME content type.</param>
    /// <returns>The canonical supported image MIME type.</returns>
    private static string NormalizeContentType(string contentType)
    {
        var normalized = contentType?.Trim().ToLowerInvariant();

        return IsSupportedContentType(normalized)
            ? normalized!
            : throw new ArgumentException(
                "Only image/jpeg, image/png, and image/webp are supported.",
                nameof(contentType));
    }

    /// <summary>
    /// Identifies permitted image MIME types for uploads and stored downloads.
    /// </summary>
    /// <param name="contentType">The image MIME content type.</param>
    /// <returns>Whether the MIME type is JPEG, PNG or WebP.</returns>
    private static bool IsSupportedContentType(string? contentType) => contentType is "image/jpeg" or "image/png" or "image/webp";

    /// <summary>
    /// Rejects empty, excessive, or control-character blob names while preserving case-sensitive metadata names.
    /// </summary>
    /// <param name="blobName">The blob name within the configured storage container.</param>
    private static void ValidateBlobName(string blobName)
    {
        if (string.IsNullOrWhiteSpace(blobName) || blobName.Length > 1024 || blobName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A blob name of 1-1024 characters without control characters is required.",
                nameof(blobName));
        }
    }

    /// <summary>
    /// Translates SDK failures into safe application errors while preserving cancellation semantics.
    /// </summary>
    /// <typeparam name="T">The operation's result value type.</typeparam>
    /// <param name="operation">The asynchronous operation to execute within the surrounding service workflow.</param>
    /// <returns>A task returning the value produced by the supplied operation.</returns>
    private static async Task<T> ExecuteStorageAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (RequestFailedException exception)
        {
            var failure = exception.Status switch
            {
                404 => BlobImageStorageFailure.NotFound,
                401 or 403 => BlobImageStorageFailure.AccessDenied,
                409 or 412 => BlobImageStorageFailure.Conflict,
                _ => BlobImageStorageFailure.Unavailable
            };

            throw new BlobImageStorageException(
                failure,
                failure switch
                {
                    BlobImageStorageFailure.NotFound => SharedResource.Get("StorageFeedback.TheImageBlobOrConfiguredContainerCouldNotBe"),
                    BlobImageStorageFailure.AccessDenied => SharedResource.Get("StorageFeedback.ImageStorageAccessWasDeniedCheckTheServerS"),
                    BlobImageStorageFailure.Conflict => SharedResource.Get("StorageFeedback.TheImageStorageOperationConflictsWithTheBlobS"),
                    _ => SharedResource.Get("StorageFeedback.ImageStorageIsTemporarilyUnavailableTryAgainLater")
                });
        }
        catch (HttpRequestException)
        {
            throw new BlobImageStorageException(
                BlobImageStorageFailure.Unavailable,
                SharedResource.Get("StorageFeedback.ImageStorageCouldNotBeReachedTryAgainLater"));
        }
    }
}
