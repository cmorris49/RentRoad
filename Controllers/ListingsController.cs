using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Services.Listings;
using RentRoad.ViewModels.Listings;

namespace RentRoad.Controllers;

/// <summary>
/// Presents public listing projections through read-only MVC actions.
/// </summary>
[Route("listings")]
public sealed class ListingsController : Controller
{
    private readonly IListingService listings;

    /// <summary>
    /// Initializes public browsing with the existing listing service.
    /// </summary>
    public ListingsController(IListingService listings)
    {
        this.listings = listings;
    }

    /// <summary>
    /// Browses or searches published listings, retaining inputs and field errors for the future Index view.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("")]
    public async Task<IActionResult> IndexAsync([FromQuery(Name = "")] ListingSearchInputViewModel? input = null,
        CancellationToken cancellationToken = default)
    {
        input ??= new ListingSearchInputViewModel();
        if (!ModelState.IsValid)
        {
            return InvalidSearch(input);
        }
        var result = await listings.SearchPublishedAsync(input.ToServiceParameters(), cancellationToken);
        if (result.Status == ListingServiceStatus.ValidationFailed)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Field, error.Message);
            }
            return InvalidSearch(input);
        }
        if (!result.IsSuccess)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Listings could not be loaded.");
        }
        return View("Index", new ListingSearchResultsViewModel(input, result.Value!));
    }

    /// <summary>
    /// Displays a published listing, returning the same not-found response for missing and nonpublic records.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> DetailsAsync([FromRoute] int id, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }
        var result = await listings.GetPublishedDetailsAsync(id, cancellationToken);
        if (result.Status == ListingServiceStatus.NotFound)
        {
            return NotFound();
        }
        if (result.Status == ListingServiceStatus.ValidationFailed)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(id), error.Message);
            }
            return ValidationProblem(ModelState);
        }
        if (!result.IsSuccess)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The listing could not be loaded.");
        }
        var similar = await listings.GetSimilarListingsAsync(id, cancellationToken);
        return View("Details", ListingDetailsViewModel.FromResult(result.Value!) with
        {
            SimilarListings = similar.Select(ListingSummaryViewModel.FromResult).ToArray()
        });
    }

    /// <summary>
    /// Returns invalid search inputs without querying listings or manufacturing a successful empty result page.
    /// </summary>
    private ViewResult InvalidSearch(ListingSearchInputViewModel input)
    {
        var result = View("Index", new ListingSearchResultsViewModel(input));
        result.StatusCode = StatusCodes.Status400BadRequest;
        return result;
    }
}
