using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Retains exact integer cents while providing explicit Canadian-dollar presentation values.
/// </summary>
public sealed record ListingPriceViewModel(long Cents)
{
    public decimal AmountCad => Cents / 100m;
    public string FormattedAmount => SharedResource.Get("Currency.CadAmount", AmountCad);
}

/// <summary>
/// Identifies a public image for future authorized delivery without exposing blob locations or original filenames.
/// </summary>
public sealed record ListingImageViewModel(int ListingImageId, string ContentType, string? AltText, int DisplayOrder, bool IsPrimary)
{
    /// <summary>
    /// Maps only presentation metadata and the image ID from an existing public projection.
    /// </summary>
    public static ListingImageViewModel FromResult(ListingImageInfo image) =>
        new(image.ListingImageId, image.ContentType, image.AltText, image.DisplayOrder, image.IsPrimary);
}

/// <summary>
/// Describes a date period whose start is inclusive and whose end is exclusive, without private block notes.
/// </summary>
public sealed record ListingAvailabilityViewModel(DateOnly StartDate, DateOnly EndDate)
{
    public DateOnly LastIncludedDate => EndDate.AddDays(-1);

    /// <summary>
    /// Formats the actual included dates without presenting the exclusive end boundary as an available day.
    /// </summary>
    public string DisplayRange => StartDate == LastIncludedDate
        ? SharedResource.Get("Availability.SingleDate", StartDate)
        : SharedResource.Get("Availability.DateRange", StartDate, LastIncludedDate);

    /// <summary>
    /// Preserves the existing half-open availability date convention in the presentation contract.
    /// </summary>
    public static ListingAvailabilityViewModel FromResult(ListingAvailabilityPeriod period) => new(period.StartDate, period.EndDate);
}

/// <summary>Shares public date periods between listing details and the request form, excluding private block reasons.</summary>
public sealed record PublicListingAvailabilityViewModel(IReadOnlyList<ListingAvailabilityViewModel> Windows,
    IReadOnlyList<ListingAvailabilityViewModel> Blocks);
