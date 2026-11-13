using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Services.Listings;

namespace RentRoad.Controllers;

/// <summary>
/// Streams authorized image bytes through the application without exposing private Azure locations or tokens.
/// </summary>
[Route("listings/{listingId:int}/images")]
public sealed class ListingImageContentController : Controller
{
    private readonly IListingImageService images;

    /// <summary>
    /// Initializes image delivery using the service that authorizes visibility and resolves storage metadata.
    /// </summary>
    /// <param name="images">The service used to authorize and manage listing images.</param>
    public ListingImageContentController(IListingImageService images)
    {
        this.images = images;
    }

    /// <summary>
    /// Serves published images anonymously and private images only to their owner, with response caching disabled.
    /// </summary>
    /// <param name="listingId">The identifier of the listing selected by the request.</param>
    /// <param name="imageId">The identifier of the listing image selected by the route.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    [AllowAnonymous]
    [HttpGet("{imageId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetAsync(
        [FromRoute] int listingId,
        [FromRoute] int imageId,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(modelStateDictionary: ModelState, title: SharedResource.Get("Validation.Summary"));
        }

        var result = await images.DownloadAsync(listingId, imageId, User, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.Status switch
            {
                ListingServiceStatus.NotFound => NotFound(),
                ListingServiceStatus.Unauthenticated => Unauthorized(),
                ListingServiceStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
                ListingServiceStatus.ValidationFailed => BadRequest(),
                ListingServiceStatus.Conflict => StatusCode(StatusCodes.Status409Conflict),
                _ =>
                    Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: SharedResource.Get("ImageFeedback.TheImageIsTemporarilyUnavailable"))
            };
        }

        var downloaded = result.Value!;

        try
        {
            var file = File(downloaded.Content, downloaded.ContentType);

            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.XContentTypeOptions = "nosniff";
            Response.RegisterForDisposeAsync(downloaded);
            return file;
        }
        catch
        {
            await downloaded.DisposeAsync();
            throw;
        }
    }
}
