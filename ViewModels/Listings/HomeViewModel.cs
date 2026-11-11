namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Supplies the Home search field and bounded public card selection.
/// </summary>
public sealed class HomeViewModel
{
    public string? SearchText { get; set; }
    public IReadOnlyList<ListingSummaryViewModel> PopularListings { get; init; } = Array.Empty<ListingSummaryViewModel>();
}
