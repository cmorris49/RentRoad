using RentRoad.Models;
using RentRoad.Models.Enums;
using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Binds only editable scalar listing fields, allowing incomplete drafts and leaving business validation to the service.
/// </summary>
public class ListingFormViewModel
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public ListingCategory? Category { get; set; }
    public ItemCondition? Condition { get; set; }
    public long? DailyPriceCents { get; set; }

    /// <summary>
    /// Presents CAD dollars while retaining exact integer cents and rejecting fractional cents or overflow during binding.
    /// </summary>
    public decimal? DailyPriceCad
    {
        get => DailyPriceCents / 100m;
        set => DailyPriceCents = ConvertCadToCents(value);
    }

    public string? PickupArea { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>
    /// Initializes an incomplete, scalar-only listing form.
    /// </summary>
    public ListingFormViewModel()
    {
    }

    /// <summary>
    /// Converts optional dollar input without rounding or exceeding the integer-cent storage range.
    /// </summary>
    private static long? ConvertCadToCents(decimal? amount)
    {
        if (!amount.HasValue)
        {
            return null;
        }
        if (amount > long.MaxValue / 100m || amount < long.MinValue / 100m)
        {
            throw new FormatException(SharedResource.Get("ListingEditor.PriceRange"));
        }
        var cents = amount.Value * 100m;
        if (cents != decimal.Truncate(cents))
        {
            throw new FormatException(SharedResource.Get("ListingEditor.PricePrecision"));
        }
        return (long)cents;
    }

    /// <summary>
    /// Preserves submitted scalar fields in a server-enriched edit response.
    /// </summary>
    protected ListingFormViewModel(ListingFormViewModel input)
    {
        Title = input.Title;
        Description = input.Description;
        Category = input.Category;
        Condition = input.Condition;
        DailyPriceCents = input.DailyPriceCents;
        PickupArea = input.PickupArea;
        Latitude = input.Latitude;
        Longitude = input.Longitude;
    }

    /// <summary>
    /// Explicitly maps editable fields, retaining existing availability and excluding IDs, status, timestamps, and images.
    /// </summary>
    public ListingUpdate ToServiceUpdate() => new()
    {
        Title = Title, Description = Description, Category = Category, Condition = Condition,
        DailyPriceCents = DailyPriceCents, PickupArea = PickupArea, Latitude = Latitude, Longitude = Longitude,
        AvailabilityWindows = null
    };

    /// <summary>
    /// Copies editable scalar values from an owner-authorized service result without exposing its relationships.
    /// </summary>
    public static ListingFormViewModel FromListing(Listing listing) => new()
    {
        Title = listing.Title, Description = listing.Description, Category = listing.Category, Condition = listing.Condition,
        DailyPriceCents = listing.DailyPriceCents, PickupArea = listing.PickupArea, Latitude = listing.Latitude, Longitude = listing.Longitude
    };
}

/// <summary>
/// Represents the create-draft form using the same editable scalar contract as editing.
/// </summary>
public sealed class ListingCreateViewModel : ListingFormViewModel
{
}
