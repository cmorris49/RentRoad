namespace RentRoad.Services.Storage;

/// <summary>
/// Describes successfully stored bytes for later integration with ListingImage metadata.
/// </summary>
public sealed record StoredBlobImage(string ContainerName, string BlobName, string ContentType, long SizeBytes);

/// <summary>
/// Owns an authenticated download stream without exposing an Azure address or access token.
/// </summary>
public sealed class BlobImageDownload : IAsyncDisposable
{
    public Stream Content { get; }
    public string ContentType { get; }
    public long SizeBytes { get; }

    /// <summary>
    /// Transfers ownership of the downloaded stream to this result.
    /// </summary>
    public BlobImageDownload(Stream content, string contentType, long sizeBytes)
    {
        Content = content;
        ContentType = contentType;
        SizeBytes = sizeBytes;
    }

    /// <summary>
    /// Releases the download stream and its underlying network resources.
    /// </summary>
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
