using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RentRoad.Data;
using RentRoad.Models;
using RentRoad.Models.Enums;
using RentRoad.Services.Storage;

namespace RentRoad.Services.Listings;

/// <summary>
/// Coordinates owned listing image metadata and private blob storage without holding SQL locks during storage requests.
/// </summary>
public sealed class ListingImageService : IListingImageService
{
    private const int CLEANUP_ATTEMPTS = 2;
    private static readonly TimeSpan recoveryTimeout = TimeSpan.FromSeconds(15);
    private readonly ApplicationDbContext context;
    private readonly IBlobImageStorageService storage;
    private readonly TimeProvider clock;
    private readonly ILogger<ListingImageService> logger;
    private readonly string containerName;

    /// <summary>
    /// Initializes image orchestration using the existing context, storage service, UTC clock, and safe operational logging.
    /// </summary>
    public ListingImageService(ApplicationDbContext context, IBlobImageStorageService storage, TimeProvider clock,
        IOptions<BlobImageStorageOptions> options, ILogger<ListingImageService> logger)
    {
        this.context = context;
        this.storage = storage;
        this.clock = clock;
        this.logger = logger;
        containerName = options.Value.ContainerName;
    }

    /// <summary>
    /// Authorizes before uploading, then rechecks ownership and listing state before saving server-generated metadata.
    /// </summary>
    public async Task<ListingServiceResult<ListingImage>> UploadAsync(int listingId, ClaimsPrincipal? user, Stream? content,
        string? contentType, string? originalFileName = null, string? altText = null, CancellationToken cancellationToken = default)
    {
        var ready = ListingOperationScope.CheckCallerState(context, listingId, user);
        if (!ready.IsSuccess)
        {
            return ListingServiceResult<ListingImage>.Failure(ready.Status, ready.Errors.ToArray());
        }
        var owned = await ReadOwnedListingAsync(listingId, user, cancellationToken);
        if (!owned.IsSuccess)
        {
            return ListingServiceResult<ListingImage>.Failure(owned.Status, owned.Errors.ToArray());
        }
        if (owned.Value!.Status == ListingStatus.Archived)
        {
            return Failure<ListingImage>(ListingServiceStatus.InvalidState, "Archived listings cannot be edited.");
        }
        var candidate = new ListingImage
        {
            ListingId = listingId,
            OriginalFileName = NormalizeFileName(originalFileName),
            AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim()
        };
        var errors = ValidateText(candidate);
        if (content is null || string.IsNullOrWhiteSpace(contentType))
        {
            errors.Add(new(nameof(Listing.Images), "An image stream and content type are required."));
        }
        if (errors.Count > 0)
        {
            return ListingServiceResult<ListingImage>.Failure(ListingServiceStatus.ValidationFailed, errors.ToArray());
        }

        StoredBlobImage stored;
        try
        {
            stored = await storage.UploadAsync(content!, contentType!, cancellationToken);
        }
        catch (ArgumentException)
        {
            return Failure<ListingImage>(ListingServiceStatus.ValidationFailed, "Choose a nonempty JPEG, PNG, or WebP image within the configured size limit.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure("UploadStorage", listingId, null, exception);
            return Failure<ListingImage>(ListingServiceStatus.StorageFailed, "The image could not be uploaded. Check storage configuration or try again.");
        }
        candidate.ContainerName = stored.ContainerName;
        candidate.BlobName = stored.BlobName;
        candidate.ContentType = stored.ContentType;
        candidate.SizeBytes = stored.SizeBytes;
        var attempt = new UploadAttempt();
        var validation = new List<ValidationResult>();
        Validator.TryValidateObject(candidate, new ValidationContext(candidate), validation, validateAllProperties: true);
        if (validation.Count > 0 || stored.SizeBytes <= 0 || stored.ContentType is not ("image/jpeg" or "image/png" or "image/webp")
            || !string.Equals(stored.ContainerName, containerName, StringComparison.Ordinal))
        {
            return await RecoverUploadAsync(candidate, attempt, Failure<ListingImage>(ListingServiceStatus.StorageFailed,
                "Storage returned invalid image metadata."));
        }

        try
        {
            var result = await MutateAsync(listingId, user, async (database, listing, images, token) =>
            {
                var existing = await database.ListingImages.AsNoTracking().SingleOrDefaultAsync(image =>
                    image.ContainerName == stored.ContainerName && image.BlobName == stored.BlobName, token);
                if (existing is not null)
                {
                    return attempt.SaveAttempted && existing.ListingId == listingId
                        ? ListingServiceResult<ListingImage>.Success(existing)
                        : Failure<ListingImage>(ListingServiceStatus.Conflict, "This storage image is already associated with a listing.");
                }
                NormalizeOrder(images);
                var image = new ListingImage
                {
                    ListingId = listingId, ContainerName = stored.ContainerName, BlobName = stored.BlobName,
                    ContentType = stored.ContentType, SizeBytes = stored.SizeBytes,
                    OriginalFileName = candidate.OriginalFileName, AltText = candidate.AltText,
                    CreatedAtUtc = clock.GetUtcNow().UtcDateTime, DisplayOrder = images.Count,
                    IsPrimary = !images.Any(existingImage => existingImage.IsPrimary)
                };
                database.ListingImages.Add(image);
                listing.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
                attempt.SaveAttempted = true;
                await database.SaveChangesAsync(token);
                return ListingServiceResult<ListingImage>.Success(image);
            }, cancellationToken);
            return result.IsSuccess ? result : await RecoverUploadAsync(candidate, attempt, result);
        }
        catch (OperationCanceledException)
        {
            await RecoverUploadAsync(candidate, attempt, Failure<ListingImage>(ListingServiceStatus.PersistenceFailed, "Image metadata saving was cancelled."));
            throw;
        }
    }

    /// <summary>
    /// Commits safe metadata removal before attempting blob deletion, retaining publication requirements and primary order.
    /// </summary>
    public async Task<ListingServiceResult<ListingImageDeletion>> DeleteAsync(int listingId, int imageId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        ListingImage? planned = null;
        ListingServiceResult<ListingImage> result;
        try
        {
            result = await MutateAsync(listingId, user, async (database, listing, images, token) =>
            {
                var selected = images.SingleOrDefault(image => image.ListingImageId == imageId);
                if (selected is null)
                {
                    return MissingImage<ListingImage>(imageId);
                }
                if (!string.Equals(selected.ContainerName, containerName, StringComparison.Ordinal))
                {
                    return Failure<ListingImage>(ListingServiceStatus.InvalidState, "The image belongs to a different configured storage container.");
                }
                var remaining = images.Where(image => image.ListingImageId != imageId).ToList();
                if (listing.Status is ListingStatus.Published or ListingStatus.Unavailable && !remaining.Any(ListingService.HasStoredImageMetadata))
                {
                    return Failure<ListingImage>(ListingServiceStatus.ValidationFailed, "A published or unavailable listing must retain at least one stored image.");
                }
                planned = selected;
                database.ListingImages.Remove(selected);
                await database.SaveChangesAsync(token);
                NormalizeOrder(remaining);
                if (remaining.Count > 0 && !remaining.Any(image => image.IsPrimary))
                {
                    remaining[0].IsPrimary = true;
                }
                listing.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
                await database.SaveChangesAsync(token);
                return ListingServiceResult<ListingImage>.Success(selected);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (planned is not null)
            {
                await CompleteDeletionAsync(planned, metadataCommitted: false);
            }
            throw;
        }
        if (planned is null)
        {
            return ListingServiceResult<ListingImageDeletion>.Failure(result.Status, result.Errors.ToArray());
        }
        if (!result.IsSuccess)
        {
            var inspection = await InspectReferenceAsync(planned);
            if (!inspection.Verified)
            {
                return Failure<ListingImageDeletion>(ListingServiceStatus.ReconciliationRequired,
                    "Image removal could not be verified. Refresh the listing before retrying; storage was left untouched.");
            }
            if (inspection.Image is not null)
            {
                return ListingServiceResult<ListingImageDeletion>.Failure(result.Status, result.Errors.ToArray());
            }
            ListingOperationScope.InvalidateTrackedEntries(context, listingId);
        }
        return await CompleteDeletionAsync(planned, metadataCommitted: true);
    }

    /// <summary>
    /// Clears the prior primary flag before assigning its replacement inside the same SQL transaction.
    /// </summary>
    public Task<ListingServiceResult<ListingImage>> SetPrimaryAsync(int listingId, int imageId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default) => MutateAsync(listingId, user, async (database, listing, images, token) =>
    {
        var selected = images.SingleOrDefault(image => image.ListingImageId == imageId);
        if (selected is null)
        {
            return MissingImage<ListingImage>(imageId);
        }
        if (!selected.IsPrimary)
        {
            foreach (var image in images)
            {
                image.IsPrimary = false;
            }
            await database.SaveChangesAsync(token);
            selected.IsPrimary = true;
            listing.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            await database.SaveChangesAsync(token);
        }
        return ListingServiceResult<ListingImage>.Success(selected);
    }, cancellationToken);

    /// <summary>
    /// Validates a complete image ID permutation and persists deterministic contiguous display positions.
    /// </summary>
    public Task<ListingServiceResult<IReadOnlyList<ListingImage>>> SetDisplayOrderAsync(int listingId, IReadOnlyList<int>? imageIds,
        ClaimsPrincipal? user, CancellationToken cancellationToken = default)
    {
        var snapshot = imageIds?.ToArray();
        return MutateAsync<IReadOnlyList<ListingImage>>(listingId, user, async (database, listing, images, token) =>
        {
            if (snapshot is null || snapshot.Length != images.Count || snapshot.Distinct().Count() != snapshot.Length
                || snapshot.Any(id => id <= 0) || !snapshot.ToHashSet().SetEquals(images.Select(image => image.ListingImageId)))
            {
                return Failure<IReadOnlyList<ListingImage>>(ListingServiceStatus.ValidationFailed,
                    "Supply every current image ID for this listing exactly once. Refresh the images if they changed.");
            }
            var byId = images.ToDictionary(image => image.ListingImageId);
            var ordered = snapshot.Select(id => byId[id]).ToList();
            NormalizeOrder(ordered);
            listing.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            await database.SaveChangesAsync(token);
            return ListingServiceResult<IReadOnlyList<ListingImage>>.Success(ordered);
        }, cancellationToken);
    }

    /// <summary>
    /// Retrieves detached metadata in display order after authenticating the owner, including archived listings.
    /// </summary>
    public async Task<ListingServiceResult<IReadOnlyList<ListingImage>>> GetImagesAsync(int listingId, ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var database = ListingOperationScope.CreateContext(context);
            var owned = await ListingOperationScope.GetOwnedListingAsync(database, listingId, user, cancellationToken);
            if (!owned.IsSuccess)
            {
                return ListingServiceResult<IReadOnlyList<ListingImage>>.Failure(owned.Status, owned.Errors.ToArray());
            }
            var images = await database.ListingImages.AsNoTracking().Where(image => image.ListingId == listingId)
                .OrderBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId).ToListAsync(cancellationToken);
            return ListingServiceResult<IReadOnlyList<ListingImage>>.Success(images);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure("ReadImages", listingId, null, exception);
            return Failure<IReadOnlyList<ListingImage>>(ListingServiceStatus.PersistenceFailed, "Listing images could not be loaded. Try again later.");
        }
    }

    /// <summary>
    /// Shares owner and archived-state checks across serializable metadata mutations without invoking blob storage in retries.
    /// </summary>
    private async Task<ListingServiceResult<T>> MutateAsync<T>(int listingId, ClaimsPrincipal? user,
        Func<ApplicationDbContext, Listing, List<ListingImage>, CancellationToken, Task<ListingServiceResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ListingOperationScope.ExecuteAsync(context, listingId, user, async (database, token) =>
            {
                var owned = await ListingOperationScope.GetOwnedListingAsync(database, listingId, user, token);
                if (!owned.IsSuccess)
                {
                    return ListingServiceResult<T>.Failure(owned.Status, owned.Errors.ToArray());
                }
                if (owned.Value!.Status == ListingStatus.Archived)
                {
                    return Failure<T>(ListingServiceStatus.InvalidState, "Archived listings cannot be edited.");
                }
                var images = await database.ListingImages.Where(image => image.ListingId == listingId)
                    .OrderBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId).ToListAsync(token);
                return await operation(database, owned.Value, images, token);
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure("SaveMetadata", listingId, null, exception);
            return Failure<T>(ListingServiceStatus.PersistenceFailed, "Image metadata could not be saved. Refresh the listing before retrying.");
        }
    }

    /// <summary>
    /// Reads current ownership before spending resources on an upload, without relying on cached entities.
    /// </summary>
    private async Task<ListingServiceResult<Listing>> ReadOwnedListingAsync(int listingId, ClaimsPrincipal? user, CancellationToken cancellationToken)
    {
        try
        {
            await using var database = ListingOperationScope.CreateContext(context);
            return await ListingOperationScope.GetOwnedListingAsync(database, listingId, user, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure("ReadListing", listingId, null, exception);
            return Failure<Listing>(ListingServiceStatus.PersistenceFailed, "The listing could not be loaded. Try again later.");
        }
    }

    /// <summary>
    /// Verifies ambiguous commits before compensation and never deletes a referenced blob or a different container's blob.
    /// </summary>
    private async Task<ListingServiceResult<ListingImage>> RecoverUploadAsync(ListingImage candidate, UploadAttempt attempt,
        ListingServiceResult<ListingImage> failure)
    {
        var inspection = await InspectReferenceAsync(candidate);
        if (!inspection.Verified)
        {
            return Failure<ListingImage>(ListingServiceStatus.ReconciliationRequired,
                "The upload's database outcome could not be verified. Storage was left untouched for reconciliation.");
        }
        if (inspection.Image is not null)
        {
            if (attempt.SaveAttempted && inspection.Image.ListingId == candidate.ListingId)
            {
                ListingOperationScope.InvalidateTrackedEntries(context, candidate.ListingId);
                return ListingServiceResult<ListingImage>.Success(inspection.Image);
            }
            return failure;
        }
        if (await TryDeleteBlobAsync(candidate))
        {
            return failure;
        }
        return Failure<ListingImage>(ListingServiceStatus.CleanupPending,
            "Image metadata was not saved, but blob cleanup is pending. The failure was recorded for reconciliation.");
    }

    /// <summary>
    /// Reports whether committed metadata removal also completed physical cleanup, without hiding partial success.
    /// </summary>
    private async Task<ListingServiceResult<ListingImageDeletion>> CompleteDeletionAsync(ListingImage image, bool metadataCommitted)
    {
        var inspection = await InspectReferenceAsync(image);
        if (!metadataCommitted && (!inspection.Verified || inspection.Image is not null))
        {
            return Failure<ListingImageDeletion>(ListingServiceStatus.ReconciliationRequired, "Image deletion was interrupted; storage was left untouched.");
        }
        var removed = inspection.Verified && inspection.Image is null && await TryDeleteBlobAsync(image);
        var value = new ListingImageDeletion(image.ListingImageId, true, removed);
        if (removed)
        {
            ListingOperationScope.InvalidateTrackedEntries(context, image.ListingId);
            return ListingServiceResult<ListingImageDeletion>.Success(value);
        }
        LogReconciliation(image, "DeleteCleanupPending");
        return new ListingServiceResult<ListingImageDeletion>(ListingServiceStatus.CleanupPending, value,
            new[] { new ListingValidationError(nameof(Listing.Images), "Image metadata was removed, but blob cleanup is pending. The failure was recorded for reconciliation.") });
    }

    /// <summary>
    /// Uses an independent bounded token and serializable read to avoid deleting blobs after an uncertain SQL commit.
    /// </summary>
    private async Task<(bool Verified, ListingImage? Image)> InspectReferenceAsync(ListingImage image)
    {
        using var timeout = new CancellationTokenSource(recoveryTimeout);
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var database = ListingOperationScope.CreateContext(context);
                await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var reference = await database.ListingImages.AsNoTracking().SingleOrDefaultAsync(stored =>
                    stored.ContainerName == image.ContainerName && stored.BlobName == image.BlobName, token);
                await transaction.CommitAsync(token);
                return (true, reference);
            }, timeout.Token);
        }
        catch (Exception exception)
        {
            LogFailure("VerifyReference", image.ListingId, image.ListingImageId, exception);
            LogReconciliation(image, "UnverifiedDatabaseOutcome");
            return (false, null);
        }
    }

    /// <summary>
    /// Retries idempotent cleanup with its own timeout, using only a verified server-resolved container and blob name.
    /// </summary>
    private async Task<bool> TryDeleteBlobAsync(ListingImage image)
    {
        if (!string.Equals(image.ContainerName, containerName, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(image.BlobName))
        {
            LogReconciliation(image, "ContainerMismatch");
            return false;
        }
        using var timeout = new CancellationTokenSource(recoveryTimeout);
        for (var attempt = 0; attempt < CLEANUP_ATTEMPTS && !timeout.IsCancellationRequested; attempt++)
        {
            try
            {
                await storage.DeleteAsync(image.BlobName, timeout.Token);
                return true;
            }
            catch (Exception exception)
            {
                LogFailure("DeleteBlob", image.ListingId, image.ListingImageId, exception);
            }
        }
        LogReconciliation(image, "BlobCleanupFailed");
        return false;
    }

    /// <summary>
    /// Removes path components from optional original filenames without using them as blob identifiers.
    /// </summary>
    private static string? NormalizeFileName(string? fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Applies the existing model's filename and alt-text length constraints before uploading any bytes.
    /// </summary>
    private static List<ListingValidationError> ValidateText(ListingImage image)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateProperty(image.OriginalFileName,
            new ValidationContext(image) { MemberName = nameof(ListingImage.OriginalFileName) }, results);
        Validator.TryValidateProperty(image.AltText, new ValidationContext(image) { MemberName = nameof(ListingImage.AltText) }, results);
        var errors = results.SelectMany(result => result.MemberNames.Select(field => new ListingValidationError(field, result.ErrorMessage!))).ToList();
        if (image.OriginalFileName?.Any(char.IsControl) == true || image.AltText?.Any(char.IsControl) == true)
        {
            errors.Add(new(nameof(Listing.Images), "Image filenames and alternative text must not contain control characters."));
        }
        return errors;
    }

    /// <summary>
    /// Assigns stable, contiguous display positions while preserving the requested order and primary flags.
    /// </summary>
    private static void NormalizeOrder(IReadOnlyList<ListingImage> images)
    {
        for (var index = 0; index < images.Count; index++)
        {
            images[index].DisplayOrder = index;
        }
    }

    /// <summary>
    /// Produces a safe image-specific failure without exposing storage or SQL diagnostics.
    /// </summary>
    private static ListingServiceResult<T> Failure<T>(ListingServiceStatus status, string message) =>
        ListingServiceResult<T>.Failure(status, new ListingValidationError(nameof(Listing.Images), message));

    /// <summary>
    /// Distinguishes invalid selected IDs from records absent from the owned listing.
    /// </summary>
    private static ListingServiceResult<T> MissingImage<T>(int imageId) => imageId <= 0
        ? Failure<T>(ListingServiceStatus.ValidationFailed, "Image ID must be positive.")
        : Failure<T>(ListingServiceStatus.NotFound, "The image could not be found for this listing.");

    /// <summary>
    /// Logs failure classification without retaining potentially sensitive SDK or SQL exception messages.
    /// </summary>
    private void LogFailure(string operation, int listingId, int? imageId, Exception exception) =>
        logger.LogError("Listing image operation {Operation} failed for listing {ListingId}, image {ImageId}, failure type {FailureType}.",
            operation, listingId, imageId, exception.GetType().Name);

    /// <summary>
    /// Records server-resolved blob identifiers for operational reconciliation, without credentials or signed URLs.
    /// </summary>
    private void LogReconciliation(ListingImage image, string reason) =>
        logger.LogError("Image reconciliation required: {Reason}, listing {ListingId}, image {ImageId}, container {ContainerName}, blob {BlobName}.",
            reason, image.ListingId, image.ListingImageId, image.ContainerName, image.BlobName);

    /// <summary>
    /// Remembers whether this logical upload attempted a SQL save across provider retries.
    /// </summary>
    private sealed class UploadAttempt
    {
        public bool SaveAttempted { get; set; }
    }
}
