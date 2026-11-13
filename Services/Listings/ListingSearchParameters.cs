using RentRoad.Models.Enums;

namespace RentRoad.Services.Listings;

/// <summary>
/// Specifies public listing filters, deterministic sorting, and one-based pagination.
/// </summary>
/// <remarks>
/// Prices are inclusive integer CAD cents. Every whitespace-separated search term must match
/// the title or description as a literal substring. Text comparison follows the database collation.
/// Empty search text and pickup area apply no text filter.
/// </remarks>
public sealed record ListingSearchParameters
{
    public const int DEFAULT_PAGE_SIZE = 20;
    public const int MAX_PAGE_SIZE = 100;
    public const int MAX_SEARCH_TEXT_LENGTH = 200;
    public const int MAX_SEARCH_TERMS = 10;
    public const int MAX_PICKUP_AREA_LENGTH = 150;

    public string? SearchText
    {
        get;
        init;
    }

    public ListingCategory? Category
    {
        get;
        init;
    }

    public long? MinimumDailyPriceCents
    {
        get;
        init;
    }

    public long? MaximumDailyPriceCents
    {
        get;
        init;
    }

    public ItemCondition? Condition
    {
        get;
        init;
    }

    public string? PickupArea
    {
        get;
        init;
    }

    public ListingSortOrder SortOrder
    {
        get;
        init;
    } = ListingSortOrder.NewestFirst;

    public int PageNumber
    {
        get;
        init;
    } = 1;

    public int PageSize
    {
        get;
        init;
    } = DEFAULT_PAGE_SIZE;
}

/// <summary>
/// Identifies supported public listing sort orders.
/// </summary>
public enum ListingSortOrder
{
    NewestFirst = 0,
    PriceLowToHigh = 1,
    PriceHighToLow = 2
}
