namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Labels a reusable public card row; its ID must be unique within the page.
/// </summary>
public sealed record ListingScrollerViewModel(string Id, string Heading, IReadOnlyList<ListingSummaryViewModel> Listings,
    bool ShowBrowseLink = false);
