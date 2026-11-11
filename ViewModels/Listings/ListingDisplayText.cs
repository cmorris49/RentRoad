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
    public static string Category(ListingCategory? category) =>
        SharedResource.Get(category.HasValue && Enum.IsDefined(category.Value) ? $"Category.{category.Value}" : "Category.Unspecified");

    /// <summary>
    /// Formats an item's condition for public display.
    /// </summary>
    public static string Condition(ItemCondition? condition) =>
        SharedResource.Get(condition.HasValue && Enum.IsDefined(condition.Value) ? $"Condition.{condition.Value}" : "Condition.Unspecified");
    /// <summary>
    /// Formats a listing status without changing its stored value, CSS token or editor state identifier.
    /// </summary>
    public static string Status(ListingStatus status) =>
        SharedResource.Get(Enum.IsDefined(status) ? $"ListingStatus.{status}" : "ListingStatus.Unknown");
}
