using System.Globalization;
using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>Contains only editable scalar values in exact form-compatible strings.</summary>
public sealed record ListingEditorFieldsViewModel(string? Title, string? Description, string? Category, string? Condition,
    string? DailyPriceCad, string? PickupArea, double? Latitude, double? Longitude)
{
    /// <summary>Preserves integer-cent precision when hydrating an owner editor or reviewed snapshot.</summary>
    public static ListingEditorFieldsViewModel FromUpdate(ListingUpdate input) => new(input.Title, input.Description,
        input.Category?.ToString(), input.Condition?.ToString(), input.DailyPriceCents.HasValue
            ? (input.DailyPriceCents.Value / 100m).ToString("0.00", CultureInfo.InvariantCulture) : null,
        input.PickupArea, input.Latitude, input.Longitude);
}

/// <summary>Provides secure application image URLs without storage identifiers.</summary>
public sealed record ListingEditorImageViewModel(int Id, string Url, string? AltText, bool IsPrimary, int DisplayOrder);

/// <summary>Preserves ISO calendar boundaries while also displaying the actual included dates.</summary>
public sealed record ListingEditorPeriodViewModel(int Id, DateOnly StartDate, DateOnly EndDate, string DisplayRange, string? Reason = null)
{
    public DateOnly LastDay => EndDate.AddDays(-1);
}

/// <summary>Projects a private editor snapshot without exposing Identity or Blob Storage internals.</summary>
public sealed record ListingEditorStateViewModel(string Token, int? ListingId, string Status, string OwnerDisplayName,
    ListingEditorFieldsViewModel Fields, IReadOnlyList<ListingEditorImageViewModel> Images,
    IReadOnlyList<ListingEditorPeriodViewModel> Windows, IReadOnlyList<ListingEditorPeriodViewModel> Blocks)
{
    /// <summary>Maps only owner-safe values and same-application image endpoints from a server-only snapshot.</summary>
    public static ListingEditorStateViewModel FromState(ListingEditorState state) => new(state.Token, state.Listing?.ListingId,
        state.Listing?.Status.ToString() ?? "Draft", state.OwnerDisplayName,
        ListingEditorFieldsViewModel.FromUpdate(state.Listing is null ? new ListingUpdate() : ListingFormViewModel.FromListing(state.Listing).ToServiceUpdate()),
        state.Images.OrderBy(image => image.DisplayOrder).ThenBy(image => image.ListingImageId)
            .Select(image => new ListingEditorImageViewModel(image.ListingImageId, $"/listings/{state.Listing!.ListingId}/images/{image.ListingImageId}", image.AltText, image.IsPrimary, image.DisplayOrder)).ToArray(),
        state.Listing?.AvailabilityWindows.Select(window => new ListingEditorPeriodViewModel(window.AvailabilityWindowId,
            window.StartDate, window.EndDate, new ListingAvailabilityViewModel(window.StartDate, window.EndDate).DisplayRange)).ToArray() ?? [],
        state.Listing?.AvailabilityBlocks.Select(block => new ListingEditorPeriodViewModel(block.AvailabilityBlockId,
            block.StartDate, block.EndDate, new ListingAvailabilityViewModel(block.StartDate, block.EndDate).DisplayRange, block.Reason)).ToArray() ?? []);
}

/// <summary>Describes the exact reviewed fields, private images and outstanding publication requirements.</summary>
public sealed record ListingEditorReviewViewModel(ListingEditorStateViewModel State, string? Nonce, IReadOnlyList<ListingValidationError> Errors)
{
    /// <summary>Overlays reviewed inputs without persisting existing listing edits before confirmation.</summary>
    public static ListingEditorReviewViewModel FromReview(ListingEditorReview review) => new(
        ListingEditorStateViewModel.FromState(review.State) with { Fields = ListingEditorFieldsViewModel.FromUpdate(review.Input) }, review.Nonce, review.Errors);
}
