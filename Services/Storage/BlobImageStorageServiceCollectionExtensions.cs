using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace RentRoad.Services.Storage;

/// <summary>
/// Registers listing image storage without contacting Azure or provisioning resources.
/// </summary>
public static class BlobImageStorageServiceCollectionExtensions
{
    /// <summary>
    /// Binds storage options and registers reusable SDK clients, validating configuration when storage is first resolved.
    /// </summary>
    public static IServiceCollection AddBlobImageStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BlobImageStorageOptions>()
            .Bind(configuration.GetSection(BlobImageStorageOptions.SECTION_NAME)).ValidateDataAnnotations();
        services.AddSingleton(provider => CreateContainerClient(provider.GetRequiredService<IOptions<BlobImageStorageOptions>>().Value));
        services.AddSingleton<IBlobImageStorageService, AzureBlobImageStorageService>();
        return services;
    }

    /// <summary>
    /// Constructs an authenticated client without including malformed connection strings in error messages.
    /// </summary>
    private static BlobContainerClient CreateContainerClient(BlobImageStorageOptions options)
    {
        try
        {
            return new BlobContainerClient(options.ConnectionString, options.ContainerName);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException("AzureBlobStorage:ConnectionString is invalid. Configure it through User Secrets or environment variables.");
        }
    }
}
