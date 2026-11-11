namespace RentRoad.ViewModels.Listings;

/// <summary>Provides safe image URLs and presentation text for the shared card and details gallery.</summary>
public sealed record ListingGalleryPhotoViewModel(string Url, string? AltText, bool IsPrimary);

/// <summary>Configures a compact carousel or a full gallery without exposing storage metadata.</summary>
public sealed record ListingGalleryViewModel(string Title, IReadOnlyList<ListingGalleryPhotoViewModel> Photos, bool Compact = false, string? NavigationUrl = null);
