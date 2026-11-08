using System.ComponentModel.DataAnnotations;

namespace RentRoad.Models;

public class ListingImage
{
    public int ListingImageId { get; set; }

    public int ListingId { get; set; }

    [Required]
    [StringLength(100)]
    public string ContainerName { get; set; } = string.Empty;

    [Required]
    [StringLength(1024)]
    public string BlobName { get; set; } = string.Empty;

    [StringLength(255)]
    public string? OriginalFileName { get; set; }

    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    [StringLength(250)]
    public string? AltText { get; set; }

    public bool IsPrimary { get; set; }

    public int DisplayOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Listing Listing { get; set; } = null!;
}
