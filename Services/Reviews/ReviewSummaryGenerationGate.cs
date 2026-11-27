using Microsoft.Extensions.Options;

namespace RentRoad.Services.Reviews;

/// <summary>
/// Bounds simultaneous provider requests within an application instance.
/// </summary>
public sealed class ReviewSummaryGenerationGate : IDisposable
{
    private readonly SemaphoreSlim semaphore;

    /// <summary>
    /// Creates the configured, conservatively bounded process-wide generation gate.
    /// </summary>
    /// <param name="options">The server-side concurrency settings.</param>
    public ReviewSummaryGenerationGate(IOptions<GeminiReviewSummaryOptions> options)
    {
        semaphore = new SemaphoreSlim(Math.Clamp(options.Value.MaxConcurrentGenerations, 1, 4));
    }

    /// <summary>
    /// Attempts to reserve a provider slot without queuing paid work.
    /// </summary>
    /// <returns>Whether a slot was immediately available.</returns>
    public bool TryEnter() => semaphore.Wait(0);

    /// <summary>
    /// Releases a previously reserved provider slot.
    /// </summary>
    public void Exit() => semaphore.Release();

    /// <summary>
    /// Releases the gate resources at application shutdown.
    /// </summary>
    public void Dispose() => semaphore.Dispose();
}
