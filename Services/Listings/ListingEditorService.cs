using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using RentRoad.Models;

namespace RentRoad.Services.Listings;

/// <summary>Coordinates existing listing services for intentional draft saves and server-verified publication review.</summary>
public sealed class ListingEditorService(IListingService listings, IListingImageService images, ListingEditorSessions sessions,
    UserManager<ApplicationUser> users, TimeProvider clock, ILogger<ListingEditorService> logger)
{
    /// <summary>Opens or resumes owner-bound editor metadata and reads current state without creating a database draft.</summary>
    public async Task<ListingServiceResult<ListingEditorState>> OpenAsync(ClaimsPrincipal user, int? listingId, string? token,
        CancellationToken cancellationToken = default)
    {
        var owner = await users.GetUserAsync(user);
        if (owner is null) return Failure<ListingEditorState>(ListingServiceStatus.Unauthenticated, "Sign in to manage a listing.");
        if (token is not null)
        {
            var resumed = sessions.Find(token, owner.Id);
            if (resumed is null || listingId.HasValue && resumed.ListingId != listingId)
                return Expired<ListingEditorState>();
            await resumed.Gate.WaitAsync(cancellationToken);
            try { return await ReadAsync(token, resumed, user, owner.DisplayName, cancellationToken); }
            finally { resumed.Gate.Release(); }
        }
        if (listingId.HasValue)
        {
            var owned = await listings.GetOwnerListingAsync(listingId.Value, user, cancellationToken);
            if (!owned.IsSuccess) return ListingServiceResult<ListingEditorState>.Failure(owned.Status, owned.Errors.ToArray());
        }
        var created = sessions.Create(owner.Id, listingId);
        return await ReadAsync(created.Token, created.Session, user, owner.DisplayName, cancellationToken);
    }

    /// <summary>Saves editable fields once for a new editor, or updates its existing owned listing on subsequent retries.</summary>
    public async Task<ListingServiceResult<ListingEditorState>> SaveAsync(string token, ClaimsPrincipal user, ListingUpdate input,
        CancellationToken cancellationToken = default)
    {
        var session = Find(token, user);
        if (session is null) return Expired<ListingEditorState>();
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            session.ReviewNonce = null;
            var saved = await SaveCoreAsync(session, user, input, true, cancellationToken);
            if (!saved.IsSuccess) return ListingServiceResult<ListingEditorState>.Failure(saved.Status, saved.Errors.ToArray());
            return await ReadAsync(token, session, user, null, cancellationToken);
        }
        finally { session.Gate.Release(); }
    }

    /// <summary>Reviews proposed fields without overwriting an existing listing, retaining a private receipt for final confirmation.</summary>
    public async Task<ListingServiceResult<ListingEditorReview>> ReviewAsync(string token, ClaimsPrincipal user, ListingUpdate input,
        CancellationToken cancellationToken = default)
    {
        var session = Find(token, user);
        if (session is null) return Expired<ListingEditorReview>();
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            session.ReviewNonce = null;
            var ready = await SaveCoreAsync(session, user, input, false, cancellationToken);
            if (!ready.IsSuccess) return ListingServiceResult<ListingEditorReview>.Failure(ready.Status, ready.Errors.ToArray());
            var validation = await listings.ValidateProposedForPublicationAsync(session.ListingId!.Value, user, input, cancellationToken);
            if (!validation.IsSuccess && validation.Status != ListingServiceStatus.ValidationFailed)
                return ListingServiceResult<ListingEditorReview>.Failure(validation.Status, validation.Errors.ToArray());
            var state = await ReadAsync(token, session, user, null, cancellationToken);
            if (!state.IsSuccess) return ListingServiceResult<ListingEditorReview>.Failure(state.Status, state.Errors.ToArray());
            session.ReviewInput = input;
            session.ReviewFingerprint = Fingerprint(state.Value!);
            session.ReviewNonce = validation.IsSuccess ? Guid.NewGuid().ToString("N") : null;
            session.ReviewExpires = clock.GetUtcNow().AddMinutes(15);
            return ListingServiceResult<ListingEditorReview>.Success(new(state.Value!, input, session.ReviewNonce, validation.Errors));
        }
        finally { session.Gate.Release(); }
    }

    /// <summary>Confirms only a reviewed, unchanged owner snapshot, then reuses normal update and transactional publication validation.</summary>
    public async Task<ListingServiceResult<ListingEditorState>> ConfirmAsync(string token, string nonce, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var session = Find(token, user);
        if (session is null) return Expired<ListingEditorState>();
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(nonce) && session.ConfirmedNonce == nonce)
                return await ReadAsync(token, session, user, null, cancellationToken);
            if (session.ReviewNonce is null || session.ReviewNonce != nonce || session.ReviewExpires < clock.GetUtcNow())
                return Failure<ListingEditorState>(ListingServiceStatus.Conflict, "Review the listing again before confirming publication.");
            var state = await ReadAsync(token, session, user, null, cancellationToken);
            if (!state.IsSuccess) return state;
            if (Fingerprint(state.Value!) != session.ReviewFingerprint)
            {
                session.ReviewNonce = null;
                return Failure<ListingEditorState>(ListingServiceStatus.Conflict, "The listing, photos or dates changed. Review them again before publishing.");
            }
            session.ReviewNonce = null;
            var updated = await listings.UpdateAsync(session.ListingId!.Value, user, session.ReviewInput!, cancellationToken);
            if (!updated.IsSuccess) return ListingServiceResult<ListingEditorState>.Failure(updated.Status, updated.Errors.ToArray());
            var published = await listings.PublishAsync(session.ListingId.Value, user, cancellationToken);
            if (!published.IsSuccess) return ListingServiceResult<ListingEditorState>.Failure(published.Status, published.Errors.ToArray());
            session.ConfirmedNonce = nonce;
            return await ReadAsync(token, session, user, null, cancellationToken);
        }
        finally { session.Gate.Release(); }
    }

    /// <summary>Resolves a receipt from the authenticated Identity principal, never a submitted owner identifier.</summary>
    private ListingEditorSession? Find(string token, ClaimsPrincipal user)
    {
        var ownerId = ListingOperationScope.GetOwnerId(user);
        return ownerId is null ? null : sessions.Find(token, ownerId);
    }

    /// <summary>Creates only after an explicit mutation; ambiguous first saves block automatic creation retries.</summary>
    private async Task<ListingServiceResult<bool>> SaveCoreAsync(ListingEditorSession session, ClaimsPrincipal user, ListingUpdate input,
        bool updateExisting, CancellationToken cancellationToken)
    {
        if (session.CreationUncertain)
            return Failure<bool>(ListingServiceStatus.ReconciliationRequired, "The draft save outcome is uncertain. Check My Listings before starting another listing; this editor will not create a duplicate.");
        if (session.ListingId.HasValue && !updateExisting) return ListingServiceResult<bool>.Success(true);
        var creating = !session.ListingId.HasValue;
        try
        {
            var result = creating ? await listings.CreateDraftAsync(user, input, cancellationToken)
                : await listings.UpdateAsync(session.ListingId!.Value, user, input, cancellationToken);
            if (!result.IsSuccess) return ListingServiceResult<bool>.Failure(result.Status, result.Errors.ToArray());
            session.ListingId = result.Value!.ListingId;
            return ListingServiceResult<bool>.Success(true);
        }
        catch (Exception exception)
        {
            session.CreationUncertain = creating;
            if (exception is OperationCanceledException) throw;
            logger.LogError(exception, "An intentional listing editor save could not be confirmed.");
            return Failure<bool>(creating ? ListingServiceStatus.ReconciliationRequired : ListingServiceStatus.PersistenceFailed,
                "The save could not be confirmed. Check the saved listing before retrying.");
        }
    }

    /// <summary>Composes owner-authorized server data; the controller must project it before sending any JSON.</summary>
    private async Task<ListingServiceResult<ListingEditorState>> ReadAsync(string token, ListingEditorSession session, ClaimsPrincipal user,
        string? displayName, CancellationToken cancellationToken)
    {
        displayName ??= (await users.GetUserAsync(user))?.DisplayName ?? "Listing owner";
        if (!session.ListingId.HasValue)
            return ListingServiceResult<ListingEditorState>.Success(new(token, null, Array.Empty<ListingImage>(), displayName));
        var listing = await listings.GetOwnerAvailabilityAsync(session.ListingId.Value, user, cancellationToken);
        if (!listing.IsSuccess) return ListingServiceResult<ListingEditorState>.Failure(listing.Status, listing.Errors.ToArray());
        var photos = await images.GetImagesAsync(session.ListingId.Value, user, cancellationToken);
        return photos.IsSuccess ? ListingServiceResult<ListingEditorState>.Success(new(token, listing.Value!, photos.Value!, displayName))
            : ListingServiceResult<ListingEditorState>.Failure(photos.Status, photos.Errors.ToArray());
    }

    /// <summary>Detects changes to reviewed fields, lifecycle, images, dates and private blocks without exposing the digest or underlying entities.</summary>
    private static string Fingerprint(ListingEditorState state) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        state.Listing!.ListingId, state.Listing.Title, state.Listing.Description, state.Listing.Category, state.Listing.Condition,
        state.Listing.DailyPriceCents, state.Listing.PickupArea, state.Listing.Latitude, state.Listing.Longitude,
        state.Listing.Status, state.Listing.UpdatedAtUtc, state.OwnerDisplayName,
        Images = state.Images.OrderBy(image => image.ListingImageId).Select(image => new { image.ListingImageId, image.IsPrimary, image.DisplayOrder, image.BlobName, image.ContainerName, image.AltText, image.ContentType, image.SizeBytes }),
        Windows = state.Listing.AvailabilityWindows.OrderBy(window => window.AvailabilityWindowId).Select(window => new { window.AvailabilityWindowId, window.StartDate, window.EndDate }),
        Blocks = state.Listing.AvailabilityBlocks.OrderBy(block => block.AvailabilityBlockId).Select(block => new { block.AvailabilityBlockId, block.StartDate, block.EndDate, block.Reason })
    })));

    /// <summary>Returns a safe error without silently allocating another draft for an expired or foreign editor.</summary>
    private static ListingServiceResult<T> Expired<T>() => Failure<T>(ListingServiceStatus.Conflict,
        "This editor session expired or is unavailable. Keep your entered values and check My Listings before opening a new editor.");

    /// <summary>Creates an actionable orchestration error using the existing service result contract.</summary>
    private static ListingServiceResult<T> Failure<T>(ListingServiceStatus status, string message) =>
        ListingServiceResult<T>.Failure(status, new ListingValidationError(string.Empty, message));
}

/// <summary>Contains server-only authorized entities, which must never be serialized directly to a client.</summary>
public sealed record ListingEditorState(string Token, Listing? Listing, IReadOnlyList<ListingImage> Images, string OwnerDisplayName);

/// <summary>Retains proposed fields and publication feedback alongside the server review receipt.</summary>
public sealed record ListingEditorReview(ListingEditorState State, ListingUpdate Input, string? Nonce, IReadOnlyList<ListingValidationError> Errors);
