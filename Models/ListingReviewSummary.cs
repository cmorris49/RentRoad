namespace RentRoad.Models;

/// <summary>
/// Stores one derived review summary and a short-lived generation claim per listing.
/// </summary>
public sealed class ListingReviewSummary
{
    public int ListingId { get; set; }
    public string? SummaryText { get; set; }
    public string? ContentFingerprint { get; set; }
    public string? Model { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public int SummarizedReviewCount { get; set; }
    public Guid? GenerationId { get; set; }
    public DateTime? GenerationExpiresAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? AttemptWindowStartedAtUtc { get; set; }
    public int AttemptsInWindow { get; set; }
}
