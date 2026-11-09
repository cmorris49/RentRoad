using System.ComponentModel.DataAnnotations;
using RentRoad.Models;
using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Binds an uploaded file and alternative text, excluding ownership and all storage identifiers.
/// </summary>
public class ListingImageUploadViewModel
{
    [Required(ErrorMessage = "Choose an image to upload.")]
    public IFormFile? File { get; set; }

    public string? AltText { get; set; }
}

/// <summary>
/// Binds only a requested image ID order; the existing service validates membership, completeness, and duplicates.
/// </summary>
public sealed class ListingImageOrderViewModel
{
    [Required(ErrorMessage = "Supply the listing's image order.")]
    public List<int>? ImageIds { get; set; }
}

/// <summary>
/// Exposes owner-safe metadata while reusing public image presentation fields and omitting blob/container identifiers.
/// </summary>
public sealed record ListingManagedImageViewModel(ListingImageViewModel Image, string? OriginalFileName, long SizeBytes, DateTime CreatedAtUtc)
{
    /// <summary>
    /// Projects metadata from an owner-authorized service result without returning the underlying entity.
    /// </summary>
    public static ListingManagedImageViewModel FromListing(ListingImage image) => new(
        new ListingImageViewModel(image.ListingImageId, image.ContentType, image.AltText, image.DisplayOrder, image.IsPrimary),
        image.OriginalFileName, image.SizeBytes, image.CreatedAtUtc);
}

/// <summary>
/// Describes an operation outcome, retaining confirmed partial-deletion state without treating it as complete success.
/// </summary>
public sealed record ListingImageOperationViewModel(ListingServiceStatus Status, bool? MetadataRemoved, bool? BlobRemoved,
    IReadOnlyList<ListingValidationError> Errors);

/// <summary>
/// Combines owner-safe image metadata with form values and explicit operation outcomes for the future management view.
/// </summary>
public sealed class ListingImageManagementViewModel : ListingImageUploadViewModel
{
    public int ListingId { get; }
    public IReadOnlyList<ListingManagedImageViewModel> Images { get; }
    public IReadOnlyList<int> ImageIds { get; }
    public ListingImageOperationViewModel? Outcome { get; }

    /// <summary>
    /// Maps authorized metadata and retains alt text/order inputs while requiring file reselection after an unsuccessful upload.
    /// </summary>
    public ListingImageManagementViewModel(int listingId, IReadOnlyList<ListingImage> images,
        ListingImageUploadViewModel? upload = null, ListingImageOrderViewModel? order = null, ListingImageOperationViewModel? outcome = null)
    {
        ListingId = listingId;
        Images = images.Select(ListingManagedImageViewModel.FromListing).ToArray();
        AltText = upload?.AltText;
        ImageIds = order?.ImageIds?.ToArray() ?? images.Select(image => image.ListingImageId).ToArray();
        Outcome = outcome;
    }
}
