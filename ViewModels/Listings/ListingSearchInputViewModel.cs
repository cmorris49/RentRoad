using System.Globalization;
using RentRoad.Models.Enums;
using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Binds flat public search query parameters without accepting listing entities or account information.
/// </summary>
/// <remarks>Business validation remains in IListingService. Prices are inclusive integer CAD cents.</remarks>
public class ListingSearchInputViewModel
{
    public string? SearchText { get; set; }
    public ListingCategory? Category { get; set; }
    public long? MinimumDailyPriceCents { get; set; }
    public long? MaximumDailyPriceCents { get; set; }
    public ItemCondition? Condition { get; set; }
    public string? PickupArea { get; set; }
    public ListingSortOrder SortOrder { get; set; } = ListingSortOrder.NewestFirst;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = ListingSearchParameters.DEFAULT_PAGE_SIZE;

    /// <summary>
    /// Initializes the same browsing defaults as the public listing service.
    /// </summary>
    public ListingSearchInputViewModel()
    {
    }

    /// <summary>
    /// Copies input values into a result page without retaining a mutable input-model reference.
    /// </summary>
    protected ListingSearchInputViewModel(ListingSearchInputViewModel input)
    {
        SearchText = input.SearchText;
        Category = input.Category;
        MinimumDailyPriceCents = input.MinimumDailyPriceCents;
        MaximumDailyPriceCents = input.MaximumDailyPriceCents;
        Condition = input.Condition;
        PickupArea = input.PickupArea;
        SortOrder = input.SortOrder;
        PageNumber = input.PageNumber;
        PageSize = input.PageSize;
    }

    /// <summary>
    /// Maps supplied filters directly into the existing service contract without changing their meaning.
    /// </summary>
    public ListingSearchParameters ToServiceParameters() => new()
    {
        SearchText = SearchText, Category = Category, MinimumDailyPriceCents = MinimumDailyPriceCents,
        MaximumDailyPriceCents = MaximumDailyPriceCents, Condition = Condition, PickupArea = PickupArea,
        SortOrder = SortOrder, PageNumber = PageNumber, PageSize = PageSize
    };

    /// <summary>
    /// Supplies flat query values for a page link while retaining every filter, sort order, and page size.
    /// </summary>
    /// <remarks>Use this dictionary with asp-all-route-data so MVC encodes page-link query values.</remarks>
    public Dictionary<string, string> GetPageRouteValues(int pageNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        var values = new Dictionary<string, string>
        {
            [nameof(PageNumber)] = pageNumber.ToString(CultureInfo.InvariantCulture),
            [nameof(PageSize)] = PageSize.ToString(CultureInfo.InvariantCulture),
            [nameof(SortOrder)] = SortOrder.ToString()
        };
        if (SearchText is not null)
        {
            values[nameof(SearchText)] = SearchText;
        }
        if (Category.HasValue)
        {
            values[nameof(Category)] = Category.Value.ToString();
        }
        if (MinimumDailyPriceCents.HasValue)
        {
            values[nameof(MinimumDailyPriceCents)] = MinimumDailyPriceCents.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (MaximumDailyPriceCents.HasValue)
        {
            values[nameof(MaximumDailyPriceCents)] = MaximumDailyPriceCents.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (Condition.HasValue)
        {
            values[nameof(Condition)] = Condition.Value.ToString();
        }
        if (PickupArea is not null)
        {
            values[nameof(PickupArea)] = PickupArea;
        }
        return values;
    }
}
