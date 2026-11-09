using RentRoad.Models.Enums;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Supplies readable enum labels shared by public search controls, categories, and listing cards.
/// </summary>
public static class ListingDisplayText
{
    /// <summary>
    /// Formats a category without changing persisted enum values or their query-string names.
    /// </summary>
    public static string Category(ListingCategory? category) => category switch
    {
        ListingCategory.CampingGear => "Camping gear",
        ListingCategory.SportsEquipment => "Sports equipment",
        null => "Uncategorized",
        _ => category.Value.ToString()
    };

    /// <summary>
    /// Formats an item's condition for public display.
    /// </summary>
    public static string Condition(ItemCondition? condition) => condition switch
    {
        ItemCondition.LikeNew => "Like new",
        null => "Condition not specified",
        _ => condition.Value.ToString()
    };
}
