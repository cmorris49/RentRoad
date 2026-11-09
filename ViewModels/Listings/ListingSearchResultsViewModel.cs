using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Combines flat editable search inputs with mapped results and optional pagination for a valid search.
/// </summary>
public sealed class ListingSearchResultsViewModel : ListingSearchInputViewModel
{
    public IReadOnlyList<ListingSummaryViewModel> Listings { get; }
    public ListingPaginationViewModel? Pagination { get; }
    public bool HasValidSearch => Pagination is not null;

    /// <summary>
    /// Maps a successful result page or retains invalid inputs without presenting them as a valid empty search.
    /// </summary>
    public ListingSearchResultsViewModel(ListingSearchInputViewModel input, ListingSearchPage? page = null) : base(input)
    {
        Listings = page?.Items.Select(ListingSummaryViewModel.FromResult).ToArray() ?? Array.Empty<ListingSummaryViewModel>();
        Pagination = page is null ? null : new ListingPaginationViewModel(page);
    }
}

/// <summary>
/// Describes one-based pagination, including empty searches and requested pages beyond the result set.
/// </summary>
public sealed class ListingPaginationViewModel
{
    private const int VISIBLE_PAGE_COUNT = 5;
    public int TotalCount { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public int TotalPages { get; }
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public int? PreviousPageNumber => HasPreviousPage ? PageNumber - 1 : null;
    public int? NextPageNumber => HasNextPage ? PageNumber + 1 : null;

    /// <summary>
    /// Supplies a bounded set of page buttons without enumerating every result page or overflowing large page numbers.
    /// </summary>
    public IEnumerable<int> VisiblePageNumbers
    {
        get
        {
            if (TotalPages == 0)
            {
                return Array.Empty<int>();
            }
            var last = (int)Math.Min(TotalPages, (long)Math.Min(PageNumber, TotalPages) + VISIBLE_PAGE_COUNT / 2);
            var first = Math.Max(1, last - VISIBLE_PAGE_COUNT + 1);
            last = (int)Math.Min(TotalPages, (long)first + VISIBLE_PAGE_COUNT - 1);
            return Enumerable.Range(first, last - first + 1);
        }
    }

    /// <summary>
    /// Copies validated pagination information from the existing service result without clamping requested pages.
    /// </summary>
    public ListingPaginationViewModel(ListingSearchPage page)
    {
        TotalCount = page.TotalCount;
        PageNumber = page.PageNumber;
        PageSize = page.PageSize;
        TotalPages = page.TotalPages;
    }
}
