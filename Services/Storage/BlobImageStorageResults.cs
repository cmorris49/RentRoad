namespace RentRoad.Services.Storage;

/// <summary>
/// Describes successfully stored bytes for later integration with ListingImage metadata.
/// </summary>
/// <param name="ContainerName">The name of the image storage container.</param>
/// <param name="BlobName">The blob name within the configured storage container.</param>
/// <param name="ContentType">The image MIME content type.</param>
/// <param name="SizeBytes">The size of the stored image in bytes.</param>
public sealed record StoredBlobImage(string ContainerName, string BlobName, string ContentType, long SizeBytes);

/// <summary>
/// Owns an authenticated download stream without exposing an Azure address or access token.
/// </summary>
public sealed class BlobImageDownload : IAsyncDisposable
{
    public Stream Content
    {
        get;
    }

    public string ContentType
    {
        get;
    }

    public long SizeBytes
    {
        get;
    }

    /// <summary>
    /// Transfers ownership of the downloaded stream to this result.
    /// </summary>
    /// <param name="content">The caller-owned stream containing the image bytes.</param>
    /// <param name="contentType">The image MIME content type.</param>
    /// <param name="sizeBytes">The size of the stored image in bytes.</param>
    public BlobImageDownload(Stream content, string contentType, long sizeBytes)
    {
        Content = content;
        ContentType = contentType;
        SizeBytes = sizeBytes;
    }

    /// <summary>
    /// Releases the download stream and its underlying network resources.
    /// </summary>
    /// <returns>A value task representing disposal of the owned download stream.</returns>
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
