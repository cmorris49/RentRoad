using System.ComponentModel.DataAnnotations;
using RentRoad.Services.Listings;
using RentRoad.Services.Rentals;
using RentRoad.ViewModels.Listings;

namespace RentRoad.ViewModels.Rentals;

/// <summary>Contains only the public listing display information needed to request a rental.</summary>
public sealed record RentalRequestListingViewModel(int ListingId, string? Title, ListingPriceViewModel DailyPrice,
    string? PickupArea, ListingImageViewModel? PrimaryImage, IReadOnlyList<ListingAvailabilityViewModel> AvailabilityWindows,
    IReadOnlyList<ListingAvailabilityViewModel> AvailabilityBlocks)
{
    /// <summary>Maps a public projection to safe presentation metadata without Identity or Blob Storage identifiers.</summary>
    public static RentalRequestListingViewModel FromResult(ListingDetails listing) => new(listing.ListingId, listing.Title,
        new(listing.DailyPriceCents), listing.PickupArea, listing.Images.OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId).Select(ListingImageViewModel.FromResult).FirstOrDefault(),
        listing.AvailabilityWindows.Select(ListingAvailabilityViewModel.FromResult).ToArray(),
        listing.AvailabilityBlocks.Select(ListingAvailabilityViewModel.FromResult).ToArray());
}

/// <summary>Enriches submitted inputs with trusted listing data, safe estimates and an optional submission outcome; errors remain in ModelState.</summary>
public sealed class RentalRequestFormViewModel : RentalRequestInputViewModel
{
    public DateOnly MinimumFirstDay { get; internal set; } = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
    public RentalRequestListingViewModel Listing { get; }
    public RentalRequestServiceStatus? SubmissionStatus { get; }

    [Display(Name = nameof(SharedResource.RentalEstimateLabel), ResourceType = typeof(SharedResource))]
    public ListingPriceViewModel? EstimatedTotal { get; }
    public string? EstimateErrorResourceKey { get; }
    public string? EstimateError => EstimateErrorResourceKey is null ? null : SharedResource.Get(EstimateErrorResourceKey);
    public string? DurationText => RentalDays.HasValue ? SharedResource.Get(RentalDays == 1
        ? "RentalRequest.DurationOne" : "RentalRequest.DurationMany", RentalDays.Value) : null;

    /// <summary>Prepares read-only display estimates from the public price, preserving all submitted inputs and guarding integer-cent overflow.</summary>
    public RentalRequestFormViewModel(ListingDetails listing, RentalRequestInputViewModel? input = null,
        RentalRequestServiceStatus? submissionStatus = null) : base(input ?? new RentalRequestInputViewModel())
    {
        Listing = RentalRequestListingViewModel.FromResult(listing);
        SubmissionStatus = submissionStatus;
        if (Listing.DailyPrice.Cents <= 0)
        {
            EstimateErrorResourceKey = "RentalRequest.InvalidPrice";
        }
        else if (RentalDays.HasValue)
        {
            try { EstimatedTotal = new(checked(Listing.DailyPrice.Cents * RentalDays.Value)); }
            catch (OverflowException) { EstimateErrorResourceKey = "RentalRequest.PriceOverflow"; }
        }
    }
}
