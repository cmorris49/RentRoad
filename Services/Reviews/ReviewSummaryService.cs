using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using RentRoad.Data;
using RentRoad.Models;
using RentRoad.Models.Enums;
using RentRoad.Services.Listings;

namespace RentRoad.Services.Reviews;

/// <summary>
/// Coordinates cache-only reads and owner-authorized, persistently claimed summary generation.
/// </summary>
public sealed class ReviewSummaryService : IReviewSummaryService
{
    private readonly ApplicationDbContext context;
    private readonly IGeminiReviewSummarizer provider;
    private readonly GeminiReviewSummaryOptions options;
    private readonly ReviewSummaryGenerationGate gate;
    private readonly TimeProvider clock;
    private readonly ILogger<ReviewSummaryService> logger;

    /// <summary>
    /// Initializes persistent cache coordination independently of the external HTTP transport.
    /// </summary>
    /// <param name="context">The application database context.</param>
    /// <param name="provider">The mockable Gemini provider boundary.</param>
    /// <param name="options">The configured limits and selected model.</param>
    /// <param name="gate">The shared process concurrency limiter.</param>
    /// <param name="clock">The UTC time source.</param>
    /// <param name="logger">The sanitized operational logger.</param>
    public ReviewSummaryService(
        ApplicationDbContext context,
        IGeminiReviewSummarizer provider,
        IOptions<GeminiReviewSummaryOptions> options,
        ReviewSummaryGenerationGate gate,
        TimeProvider clock,
        ILogger<ReviewSummaryService> logger)
    {
        this.context = context;
        this.provider = provider;
        this.options = options.Value;
        this.gate = gate;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReviewSummary> GetSummaryAsync(int listingId, CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await ReadSnapshotAsync(context, listingId, cancellationToken);

            if (snapshot is null)
            {
                return Empty(listingId, ReviewSummaryState.NotFound);
            }

            var cache = await context.ListingReviewSummaries.AsNoTracking()
                .SingleOrDefaultAsync(summary => summary.ListingId == listingId, cancellationToken);
            return Describe(snapshot, cache);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            logger.LogWarning("Review summary cache read unavailable.");
            return Empty(listingId, ReviewSummaryState.Unavailable);
        }
    }

    /// <inheritdoc />
    public async Task<ReviewSummary> GenerateAsync(
        int listingId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userId = user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Empty(listingId, ReviewSummaryState.Forbidden);
        }

        var entered = false;
        var generationId = Guid.NewGuid();
        var claimed = false;

        try
        {
            var snapshot = await ReadSnapshotAsync(context, listingId, cancellationToken);

            if (snapshot is null || snapshot.OwnerId != userId)
            {
                return Empty(listingId, ReviewSummaryState.NotFound);
            }

            var existing = await GetSummaryAsync(listingId, cancellationToken);

            if (existing.State != ReviewSummaryState.NotGenerated)
            {
                return existing;
            }

            entered = gate.TryEnter();

            if (!entered)
            {
                return existing with { State = ReviewSummaryState.Busy };
            }

            var claim = await LockedAsync(listingId, async database =>
            {
                var current = await ReadSnapshotAsync(database, listingId, cancellationToken);

                if (current is null || current.OwnerId != userId)
                {
                    return new GenerationClaim(null, Empty(listingId, ReviewSummaryState.NotFound));
                }

                var cache = await database.ListingReviewSummaries.FindAsync([listingId], cancellationToken);
                var result = Describe(current, cache);
                var now = clock.GetUtcNow().UtcDateTime;

                if (result.State != ReviewSummaryState.NotGenerated)
                {
                    return new GenerationClaim(null, result);
                }

                if (cache?.GenerationId == generationId)
                {
                    return new GenerationClaim(current, result);
                }

                if (cache?.GenerationExpiresAtUtc > now)
                {
                    return new GenerationClaim(null, result with { State = ReviewSummaryState.Busy });
                }

                if ((options.CooldownSeconds > 0 && cache?.NextAttemptAtUtc > now)
                    || (cache?.AttemptWindowStartedAtUtc > now.AddDays(-1)
                        && cache.AttemptsInWindow >= options.MaxGenerationsPerDay))
                {
                    return new GenerationClaim(null, result with { State = ReviewSummaryState.RateLimited });
                }

                if (cache is null)
                {
                    cache = new ListingReviewSummary { ListingId = listingId };
                    database.ListingReviewSummaries.Add(cache);
                }

                if (cache.AttemptWindowStartedAtUtc is null || cache.AttemptWindowStartedAtUtc <= now.AddDays(-1))
                {
                    cache.AttemptWindowStartedAtUtc = now;
                    cache.AttemptsInWindow = 0;
                }

                cache.AttemptsInWindow++;
                cache.NextAttemptAtUtc = options.CooldownSeconds == 0 ? null : now.AddSeconds(options.CooldownSeconds);
                cache.GenerationId = generationId;
                cache.GenerationExpiresAtUtc = now.AddSeconds(options.TimeoutSeconds + 60);
                await database.SaveChangesAsync(cancellationToken);
                return new GenerationClaim(current, result);
            }, cancellationToken);

            if (claim.Snapshot is null)
            {
                return claim.Result;
            }

            claimed = true;
            logger.LogInformation("Review summary generation claimed for listing {ListingId}.", listingId);
            var generated = await provider.GenerateAsync(claim.Snapshot.Input, cancellationToken);
            logger.LogInformation("Review summary provider completed with {State} for {ReviewCount} input reviews.",
                generated.State, claim.Snapshot.Input.Count);
            var text = generated.State == ReviewSummaryState.Current
                ? ReviewSummaryText.Validate(generated.Text, options.MaxSummaryCharacters) : null;

            if (text is null)
            {
                var state = generated.State == ReviewSummaryState.Current ? ReviewSummaryState.InvalidResponse : generated.State;
                logger.LogWarning("Review summary generation ended with {State}.", state);
                return claim.Result with { State = state, CanGenerate = false };
            }

            return await LockedAsync(listingId, async database =>
            {
                var current = await ReadSnapshotAsync(database, listingId, cancellationToken);
                var cache = await database.ListingReviewSummaries.FindAsync([listingId], cancellationToken);

                if (current is null)
                {
                    return Empty(listingId, ReviewSummaryState.NotFound);
                }

                if (cache?.ContentFingerprint == current.Fingerprint && cache.SummaryText == text
                    && cache.GenerationId is null)
                {
                    return Describe(current, cache);
                }

                if (cache?.GenerationId != generationId || cache.GenerationExpiresAtUtc <= clock.GetUtcNow().UtcDateTime
                    || current.Fingerprint != claim.Snapshot.Fingerprint || current.OwnerId != userId)
                {
                    return Describe(current, null) with { State = ReviewSummaryState.ReviewsChanged, CanGenerate = false };
                }

                cache.SummaryText = text;
                cache.ContentFingerprint = current.Fingerprint;
                cache.Model = options.Model;
                cache.GeneratedAtUtc = clock.GetUtcNow().UtcDateTime;
                cache.SummarizedReviewCount = claim.Snapshot.Input.Count;
                cache.GenerationId = null;
                cache.GenerationExpiresAtUtc = null;
                await database.SaveChangesAsync(cancellationToken);
                return Describe(current, cache);
            }, cancellationToken);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            logger.LogWarning("Review summary persistence unavailable; no provider retry was attempted.");
            return Empty(listingId, ReviewSummaryState.Unavailable);
        }
        finally
        {
            if (claimed)
            {
                await ReleaseClaimAsync(listingId, generationId);
            }

            if (entered)
            {
                gate.Exit();
            }
        }
    }

    /// <summary>
    /// Builds a fingerprint of every eligible review and a bounded provider input without account data.
    /// </summary>
    /// <param name="database">The context used for the snapshot.</param>
    /// <param name="listingId">The published listing identifier.</param>
    /// <param name="cancellationToken">The query cancellation token.</param>
    /// <returns>The review snapshot or null for a nonpublic listing.</returns>
    private async Task<ReviewSnapshot?> ReadSnapshotAsync(
        ApplicationDbContext database,
        int listingId,
        CancellationToken cancellationToken)
    {
        var ownerId = await database.Listings.AsNoTracking()
            .Where(listing => listing.ListingId == listingId && listing.Status == ListingStatus.Published)
            .Select(listing => listing.OwnerId).SingleOrDefaultAsync(cancellationToken);

        if (ownerId is null)
        {
            return null;
        }

        var reviews = await database.Reviews.AsNoTracking()
            .Where(review => review.RentalRequest.ListingId == listingId
                && review.RentalRequest.Status == RentalStatus.Completed)
            .OrderBy(review => review.ReviewId)
            .Select(review => new { review.ReviewId, review.Rating, review.Comment }).ToListAsync(cancellationToken);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(reviews)));
        var remaining = Math.Clamp(options.MaxInputCharacters, 100, 30000) - 2;
        var input = new List<ReviewSummaryInput>();
        var candidates = reviews.Select(review => new ReviewSummaryInput(review.Rating,
            ReviewSummaryText.Minimize(review.Comment, Math.Clamp(options.MaxCommentCharacters, 40, 2000))))
            .OrderByDescending(review => review.Comment is not null);

        foreach (var review in candidates.Take(Math.Clamp(options.MaxReviews, 2, 100)))
        {
            var cost = JsonSerializer.Serialize(review).Length + (input.Count == 0 ? 0 : 1);

            if (cost > remaining)
            {
                continue;
            }

            input.Add(review);
            remaining -= cost;
        }

        return new ReviewSnapshot(listingId, ownerId, fingerprint, reviews.Count,
            reviews.Count == 0 ? 0 : reviews.Average(review => review.Rating), input);
    }

    /// <summary>
    /// Describes only a matching validated cache entry as current AI content.
    /// </summary>
    /// <param name="snapshot">The current public review dataset.</param>
    /// <param name="cache">The optional persistent summary.</param>
    /// <returns>A public-safe summary result.</returns>
    private ReviewSummary Describe(ReviewSnapshot snapshot, ListingReviewSummary? cache)
    {
        var enough = snapshot.Input.Count(review => review.Comment is not null) >= Math.Clamp(options.MinimumComments, 2, 20);
        var current = cache?.ContentFingerprint == snapshot.Fingerprint
            && cache.GeneratedAtUtc.HasValue && !string.IsNullOrWhiteSpace(cache.Model)
            && ReviewSummaryText.Validate(cache.SummaryText, 1000) is not null;
        var state = current ? ReviewSummaryState.Current : !enough ? ReviewSummaryState.InsufficientReviews
            : !options.IsConfigured ? ReviewSummaryState.NotConfigured : ReviewSummaryState.NotGenerated;

        return new ReviewSummary(snapshot.ListingId, snapshot.AverageRating, snapshot.ReviewCount,
            current ? cache!.SummaryText! : string.Empty, current ? cache!.GeneratedAtUtc!.Value : default)
        {
            State = state,
            Model = current ? cache!.Model : null,
            SummarizedReviewCount = current ? cache!.SummarizedReviewCount : 0,
            CanGenerate = !current && enough && options.IsConfigured
        };
    }

    /// <summary>
    /// Serializes short cache mutations across instances while keeping HTTP outside database retries.
    /// </summary>
    /// <typeparam name="T">The mutation result type.</typeparam>
    /// <param name="listingId">The listing primary key used for coordination.</param>
    /// <param name="operation">The transactional cache operation.</param>
    /// <param name="cancellationToken">The database cancellation token.</param>
    /// <returns>The committed operation result.</returns>
    private Task<T> LockedAsync<T>(
        int listingId,
        Func<ApplicationDbContext, Task<T>> operation,
        CancellationToken cancellationToken) => context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
    {
        await using var database = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>((DbContextOptions<ApplicationDbContext>)context.GetService<IDbContextOptions>())
                .UseModel(context.Model).Options);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await ListingTransactionLock.AcquireAsync(database, listingId, token);
        var result = await operation(database);
        await transaction.CommitAsync(token);
        return result;
    }, cancellationToken);

    /// <summary>
    /// Releases only this request's claim, retaining the charged-attempt budget even on cancellation.
    /// </summary>
    /// <param name="listingId">The claimed listing identifier.</param>
    /// <param name="generationId">The unique claim owner.</param>
    /// <returns>Completion of best-effort bounded cleanup.</returns>
    private async Task ReleaseClaimAsync(int listingId, Guid generationId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await LockedAsync(listingId, async database =>
            {
                var cache = await database.ListingReviewSummaries.FindAsync([listingId], timeout.Token);

                if (cache?.GenerationId == generationId)
                {
                    cache.GenerationId = null;
                    cache.GenerationExpiresAtUtc = null;
                    await database.SaveChangesAsync(timeout.Token);
                }

                return true;
            }, timeout.Token);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception) || exception is OperationCanceledException)
        {
            logger.LogWarning("Review summary claim cleanup unavailable; its bounded lease will expire.");
        }
    }

    /// <summary>
    /// Identifies expected persistence failures without logging exception payloads.
    /// </summary>
    /// <param name="exception">The persistence exception.</param>
    /// <returns>Whether the failure should become a safe unavailable state.</returns>
    private static bool IsDatabaseFailure(Exception exception) =>
        exception is DbException or DbUpdateException or RetryLimitExceededException;

    /// <summary>
    /// Creates an empty result without review content for inaccessible or unavailable data.
    /// </summary>
    /// <param name="listingId">The requested listing identifier.</param>
    /// <param name="state">The safe outcome category.</param>
    /// <returns>The content-free result.</returns>
    private static ReviewSummary Empty(int listingId, ReviewSummaryState state) => new(listingId, 0, 0, string.Empty, default)
    {
        State = state
    };

    /// <summary>
    /// Carries the private snapshot used for fingerprint comparison and bounded generation input.
    /// </summary>
    /// <param name="ListingId">The listing identifier, never sent to the provider.</param>
    /// <param name="OwnerId">The server-side authorization identity, never sent to the provider.</param>
    /// <param name="Fingerprint">The hash of the complete eligible dataset.</param>
    /// <param name="ReviewCount">The complete public review count.</param>
    /// <param name="AverageRating">The complete public average rating.</param>
    /// <param name="Input">The minimized rating/comment pairs sent to the provider.</param>
    private sealed record ReviewSnapshot(int ListingId, string OwnerId, string Fingerprint,
        int ReviewCount, double AverageRating, IReadOnlyList<ReviewSummaryInput> Input);

    /// <summary>
    /// Carries a successfully claimed snapshot or a terminal cache outcome.
    /// </summary>
    /// <param name="Snapshot">The snapshot authorized for a single provider call.</param>
    /// <param name="Result">The safe cache or claim outcome.</param>
    private sealed record GenerationClaim(ReviewSnapshot? Snapshot, ReviewSummary Result);
}
