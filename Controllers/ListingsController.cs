using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Services.Listings;
using RentRoad.Services.Messaging;
using RentRoad.ViewModels.Listings;

namespace RentRoad.Controllers;

/// <summary>
/// Presents public listing projections through read-only MVC actions.
/// </summary>
[Route("listings")]
public sealed class ListingsController : Controller
{
    private readonly IListingService listings;
    private readonly IMessagingService messaging;

    /// <summary>
    /// Initializes public browsing with the existing listing service.
    /// </summary>
    public ListingsController(IListingService listings, IMessagingService messaging)
    {
        this.listings = listings;
        this.messaging = messaging;
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
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: SharedResource.Get("Validation.ListingsUnavailable"));
        }
        return View("Index", new ListingSearchResultsViewModel(input, result.Value!));
    }

    /// <summary>
    /// Displays a published listing, returning the same not-found response for missing and nonpublic records.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DetailsAsync([FromRoute] int id, CancellationToken cancellationToken = default, [FromQuery] int? conversationId = null)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(modelStateDictionary: ModelState, title: SharedResource.Get("Validation.Summary"));
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
            return ValidationProblem(modelStateDictionary: ModelState, title: SharedResource.Get("Validation.Summary"));
        }
        if (!result.IsSuccess)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: SharedResource.Get("ListingFeedback.TheListingCouldNotBeLoaded"));
        }
        int? returnConversationId = null;
        if (conversationId > 0 && User.Identity?.IsAuthenticated == true)
        {
            var conversation = await messaging.GetConversationAsync(conversationId.Value, User, cancellationToken);
            if (conversation.IsSuccess && conversation.Value?.ListingId == id) returnConversationId = conversation.Value.ConversationId;
        }
        var similar = await listings.GetSimilarListingsAsync(id, cancellationToken);
        return View("Details", ListingDetailsViewModel.FromResult(result.Value!) with
        {
            ReturnConversationId = returnConversationId,
            IsOwnedByViewer = await listings.IsOwnedByAsync(id, User, cancellationToken),
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
