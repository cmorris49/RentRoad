using RentRoad.Models;
using RentRoad.Models.Enums;
using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Combines editable scalar values with server-only listing identity, dates, status, and publication errors.
/// </summary>
/// <remarks>POST actions bind ListingFormViewModel, never this server-enriched response type.</remarks>
public sealed class ListingEditViewModel : ListingFormViewModel
{
    public int ListingId { get; }
    public DateTime CreatedAtUtc { get; }
    public DateTime UpdatedAtUtc { get; }
    public DateTime? PublishedAtUtc { get; }
    public ListingPublicationViewModel Publication { get; }

    /// <summary>
    /// Restores trusted display metadata while preserving submitted fields when supplied.
    /// </summary>
    public ListingEditViewModel(Listing listing, ListingFormViewModel? input = null,
        IReadOnlyList<ListingValidationError>? publicationErrors = null) : base(input ?? FromListing(listing))
    {
        ListingId = listing.ListingId;
        CreatedAtUtc = listing.CreatedAtUtc;
        UpdatedAtUtc = listing.UpdatedAtUtc;
        PublishedAtUtc = listing.PublishedAtUtc;
        Publication = new ListingPublicationViewModel(listing.Status, publicationErrors is null ? null : false,
            publicationErrors?.ToArray() ?? Array.Empty<ListingValidationError>());
    }
}

/// <summary>
/// Exposes trusted listing status and the latest publication-validation outcome without allowing status form binding.
/// </summary>
public sealed record ListingPublicationViewModel(ListingStatus Status, bool? IsValid, IReadOnlyList<ListingValidationError> Errors)
{
    public bool CanEdit => Status != ListingStatus.Archived;
}
