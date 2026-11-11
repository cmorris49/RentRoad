using Microsoft.Extensions.Caching.Memory;

namespace RentRoad.Services.Listings;

/// <summary>Retains bounded, owner-bound editor receipts and serializes retries within this application instance.</summary>
public sealed class ListingEditorSessions(IMemoryCache cache)
{
    /// <summary>Allocates editor metadata only; opening an editor never inserts a listing.</summary>
    internal (string Token, ListingEditorSession Session) Create(string ownerId, int? listingId)
    {
        var token = Guid.NewGuid().ToString("N");
        var session = new ListingEditorSession(ownerId, listingId);
        cache.Set("listing-editor:" + token, session, TimeSpan.FromHours(8));
        return (token, session);
    }

    /// <summary>Returns only an unexpired receipt belonging to the authenticated owner.</summary>
    internal ListingEditorSession? Find(string token, string ownerId) =>
        cache.TryGetValue("listing-editor:" + token, out ListingEditorSession? session) && session?.OwnerId == ownerId ? session : null;
}

/// <summary>Contains server-only draft and review receipts; no entity fields or session state are form-bound.</summary>
internal sealed class ListingEditorSession(string ownerId, int? listingId)
{
    internal string OwnerId { get; } = ownerId;
    internal int? ListingId { get; set; } = listingId;
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal bool CreationUncertain { get; set; }
    internal ListingUpdate? ReviewInput { get; set; }
    internal string? ReviewFingerprint { get; set; }
    internal string? ReviewNonce { get; set; }
    internal DateTimeOffset ReviewExpires { get; set; }
    internal string? ConfirmedNonce { get; set; }
}
