using RentRoad.Services.Reviews;

namespace RentRoad.ViewModels.Reviews;

/// <summary>
/// Presents editable review fields and server-selected rental choices without exposing Identity or entity graphs.
/// </summary>
public sealed class ReviewFormViewModel
{
    public int RentalRequestId { get; init; }
    public int? ListingId { get; init; }
    public int? Rating { get; init; }
    public string? Comment { get; init; }
    public string ReturnUrl { get; init; } = "/rentals/mine";
    public bool IsEditing { get; init; }
    public bool Open { get; init; }
    public string PanelId { get; init; } = "review-form";
    public IReadOnlyList<ReviewRentalChoice> Choices { get; init; } = Array.Empty<ReviewRentalChoice>();
}

/// <summary>
/// Formats the existing arithmetic average using localized number formatting and singular/plural resources.
/// </summary>
public static class ReviewDisplayText
{
    /// <summary>
    /// Produces one consistent summary for the Details badge and Reviews section.
    /// </summary>
    /// <param name="average">The unchanged arithmetic average, or null for no reviews.</param>
    /// <param name="count">The number of included reviews.</param>
    /// <returns>The localized average/count or empty-state text.</returns>
    public static string Summary(double? average, int count) => average is null || count == 0
        ? SharedResource.Get("ListingDetails.NoReviews")
        : SharedResource.Get(count == 1 ? "ListingDetails.ReviewSummarySingle" : "ListingDetails.ReviewSummary", average.Value, count);
}
