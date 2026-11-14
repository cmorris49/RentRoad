namespace RentRoad.Services.Reviews;

/// <summary>
/// Represents the data submitted for a renter review on a completed rental request.
/// </summary>
/// <param name="RentalRequestId">The rental request being reviewed.</param>
/// <param name="Rating">The review rating on a 1-to-5 scale.</param>
/// <param name="Comment">The optional free-text review comment.</param>
public sealed record ReviewSubmission(int RentalRequestId, int Rating, string? Comment);
