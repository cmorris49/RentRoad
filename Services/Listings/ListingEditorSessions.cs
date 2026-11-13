using Microsoft.Extensions.Caching.Memory;

namespace RentRoad.Services.Listings;

/// <summary>
/// Retains bounded, owner-bound editor receipts and serializes retries within this application instance.
/// </summary>
/// <param name="cache">The memory cache used to retain editor session receipts.</param>
public sealed class ListingEditorSessions(IMemoryCache cache)
{
    /// <summary>
    /// Allocates editor metadata only; opening an editor never inserts a listing.
    /// </summary>
    /// <param name="ownerId">The listing owner's identifier.</param>
    /// <param name="listingId">The existing listing identifier, or null for a new editor session.</param>
    /// <returns>The new editor token and its associated session receipt.</returns>
    internal (string Token, ListingEditorSession Session) Create(string ownerId, int? listingId)
    {
        var token = Guid.NewGuid().ToString("N");
        var session = new ListingEditorSession(ownerId, listingId);

        cache.Set("listing-editor:" + token, session, TimeSpan.FromHours(8));
        return (token, session);
    }

    /// <summary>
    /// Returns only an unexpired receipt belonging to the authenticated owner.
    /// </summary>
    /// <param name="token">The owner-bound editor session token, when supplied.</param>
    /// <param name="ownerId">The listing owner's identifier.</param>
    /// <returns>The matching owner-bound session, or null when it is unavailable.</returns>
    internal ListingEditorSession? Find(string token, string ownerId) =>
        cache.TryGetValue("listing-editor:" + token, out ListingEditorSession? session) && session?.OwnerId == ownerId
            ? session
            : null;
}

/// <summary>
/// Contains server-only draft and review receipts; no entity fields or session state are form-bound.
/// </summary>
/// <param name="ownerId">The listing owner's identifier.</param>
/// <param name="listingId">The existing listing identifier, or null for a new editor session.</param>
internal sealed class ListingEditorSession(string ownerId, int? listingId)
{
    internal string OwnerId
    {
        get;
    } = ownerId;

    internal int? ListingId
    {
        get;
        set;
    } = listingId;

    internal SemaphoreSlim Gate
    {
        get;
    } = new(1, 1);

    internal bool CreationUncertain
    {
        get;
        set;
    }

    internal ListingUpdate? ReviewInput
    {
        get;
        set;
    }

    internal string? ReviewFingerprint
    {
        get;
        set;
    }

    internal string? ReviewNonce
    {
        get;
        set;
    }

    internal DateTimeOffset ReviewExpires
    {
        get;
        set;
    }

    internal string? ConfirmedNonce
    {
        get;
        set;
    }
}
