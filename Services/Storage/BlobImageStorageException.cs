namespace RentRoad.Services.Storage;

/// <summary>
/// Classifies storage failures without exposing Azure response bodies or authentication details.
/// </summary>
public enum BlobImageStorageFailure
{
    NotFound,
    AccessDenied,
    Conflict,
    Unavailable,
    ContainerNotPrivate,
    InvalidStoredImage
}

/// <summary>
/// Reports a safe storage error for application-level handling, without retaining raw SDK exceptions.
/// </summary>
public sealed class BlobImageStorageException : Exception
{
    public BlobImageStorageFailure Failure { get; }

    /// <summary>
    /// Describes the failure using a message that contains no storage credentials or request details.
    /// </summary>
    public BlobImageStorageException(BlobImageStorageFailure failure, string message) : base(message)
    {
        Failure = failure;
    }
}
