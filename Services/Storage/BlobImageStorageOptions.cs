using System.ComponentModel.DataAnnotations;

namespace RentRoad.Services.Storage;

/// <summary>
/// Configures an existing private container and bounded, in-memory upload validation.
/// </summary>
public sealed class BlobImageStorageOptions
{
    public const string SECTION_NAME = "AzureBlobStorage";
    public const int MAX_ALLOWED_IMAGE_SIZE_BYTES = 20 * 1024 * 1024;

    [Required(ErrorMessage = "Configure AzureBlobStorage:ConnectionString through User Secrets or environment variables.")]
    public string ConnectionString
    {
        get;
        set;
    } = string.Empty;

    [Required]
    [RegularExpression("^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$",
    ErrorMessage = "ContainerName must be 3-63 lowercase letters, digits, or single hyphens, starting and ending with a letter or digit.")]
    public string ContainerName
    {
        get;
        set;
    } = "listing-images";

    [Range(1, MAX_ALLOWED_IMAGE_SIZE_BYTES)]
    public int MaxImageSizeBytes
    {
        get;
        set;
    } = 5 * 1024 * 1024;
}
