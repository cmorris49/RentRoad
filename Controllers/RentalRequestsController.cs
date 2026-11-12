using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Services.Listings;
using RentRoad.Services.Rentals;
using RentRoad.ViewModels.Rentals;

namespace RentRoad.Controllers;

/// <summary>Adapts authenticated renter submissions to the existing rental service without exposing management or decision endpoints.</summary>
[Authorize]
[Route("listings/{listingId:int}/request")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RentalRequestsController : Controller
{
    private readonly IListingService listings;
    private readonly IRentalRequestService rentals;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes request MVC using existing public listing projections and rental business services.</summary>
    public RentalRequestsController(IListingService listings, IRentalRequestService rentals, TimeProvider? timeProvider = null)
    {
        this.listings = listings;
        this.rentals = rentals;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Prepares the request view from published listing information without creating a request.</summary>
    [HttpGet("")]
    public async Task<IActionResult> CreateAsync([FromRoute] int listingId, CancellationToken cancellationToken = default)
    {
        if (listingId <= 0 || !ModelState.IsValid) return NotFound();
        var listing = await listings.GetPublishedDetailsAsync(listingId, cancellationToken);
        if (!listing.IsSuccess) return ListingFailure(listing.Status);
        if (await listings.IsOwnedByAsync(listingId, User, cancellationToken)) return Forbid();
        return FormView(new(listing.Value!), StatusCodes.Status200OK);
    }

    /// <summary>Submits only dates/times for the route listing and current principal, redirecting after new or existing pending requests.</summary>
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAsync([FromRoute] int listingId, [FromForm(Name = "")] RentalRequestInputViewModel input,
        CancellationToken cancellationToken = default)
    {
        if (listingId <= 0) return NotFound();
        TempData.Remove("RentalRequestFeedback");
        TempData.Remove("RentalRequestFeedbackListingId");
        var listing = await listings.GetPublishedDetailsAsync(listingId, cancellationToken);
        if (!listing.IsSuccess) return ListingFailure(listing.Status);
        if (!ModelState.IsValid) return FormView(new(listing.Value!, input, RentalRequestServiceStatus.ValidationFailed), StatusCodes.Status400BadRequest);
        var result = await rentals.CreateAsync(User, input.ToServiceRequest(listingId), cancellationToken);
        if (result.IsSuccess)
        {
            TempData["RentalRequestFeedbackListingId"] = listingId;
            TempData["RentalRequestFeedback"] = SharedResource.Get(result.AlreadyPending ? "RentalRequest.AlreadyPending" : "RentalRequest.Submitted");
            return RedirectToAction("Details", "Listings", new { id = listingId });
        }
        if (result.Status == RentalRequestServiceStatus.Unauthenticated) return Challenge();
        if (result.Status == RentalRequestServiceStatus.Forbidden) return Forbid();
        if (result.Status == RentalRequestServiceStatus.NotFound || result.Errors.Any(error => error.ResourceKey is
            "RentalRequest.ListingNotPublished" or "RentalRequest.ListingNotFound")) return NotFound();
        foreach (var error in result.Errors)
        {
            var key = error.ResourceKey switch
            {
                "RentalRequest.StartRequired" => "Availability.FirstDayRequired",
                "RentalRequest.EndRequired" => "Availability.LastDayRequired",
                "RentalRequest.PositiveDurationRequired" => "Availability.LastDayBeforeFirst",
                _ => error.ResourceKey
            };
            ModelState.AddModelError(FormField(error.Field), SharedResource.Get(key));
        }
        var statusCode = result.Status switch
        {
            RentalRequestServiceStatus.ValidationFailed => StatusCodes.Status400BadRequest,
            RentalRequestServiceStatus.Conflict or RentalRequestServiceStatus.ConcurrencyFailed => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        return FormView(new(listing.Value!, input, result.Status), statusCode);
    }

    /// <summary>Maps only editable backend field names to the inclusive form, leaving listing/account/state errors in the summary.</summary>
    private static string FormField(string field) => field switch
    {
        "StartDate" => nameof(RentalRequestInputViewModel.FirstDay),
        "EndDate" => nameof(RentalRequestInputViewModel.LastDay),
        nameof(RentalRequestInputViewModel.PickupTime) => nameof(RentalRequestInputViewModel.PickupTime),
        nameof(RentalRequestInputViewModel.ReturnTime) => nameof(RentalRequestInputViewModel.ReturnTime),
        _ => string.Empty
    };

    /// <summary>Returns the same missing response for absent/nonpublic listings and safe dependency failure feedback otherwise.</summary>
    private IActionResult ListingFailure(ListingServiceStatus status) => status switch
    {
        ListingServiceStatus.NotFound or ListingServiceStatus.ValidationFailed => NotFound(),
        ListingServiceStatus.Unauthenticated => Challenge(),
        ListingServiceStatus.Forbidden => Forbid(),
        _ => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: SharedResource.Get("RentalRequest.FormUnavailable"))
    };

    /// <summary>Preserves ModelState attempted values and returns the strongly typed Create view.</summary>
    private ViewResult FormView(RentalRequestFormViewModel model, int statusCode)
    {
        model.MinimumFirstDay = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);
        ViewData["Title"] = SharedResource.Get("RentalRequest.CreateHeading");
        var result = View("Create", model);
        result.StatusCode = statusCode;
        return result;
    }
}
