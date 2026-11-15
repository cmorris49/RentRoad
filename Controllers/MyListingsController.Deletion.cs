using Microsoft.AspNetCore.Mvc;
using RentRoad.ViewModels.Listings;
using RentRoad.Services.Listings;

namespace RentRoad.Controllers;

/// <summary>
/// Adapts explicitly confirmed owner deletion to the protected-history listing service.
/// </summary>
public sealed partial class MyListingsController
{
    /// <summary>
    /// Permanently deletes an eligible owned listing after explicit confirmation and antiforgery validation.
    /// </summary>
    /// <param name="id">The route-selected listing.</param>
    /// <param name="confirmed">Whether the owner explicitly confirmed this irreversible operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A management redirect or an appropriate failure with readable feedback.</returns>
    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAsync(
        [FromRoute] int id,
        [FromForm] bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || id <= 0 || !confirmed)
        {
            return BadRequest(SharedResource.Get("ListingDeletion.ConfirmRequired"));
        }

        var result = await listings.DeleteAsync(id, User, cancellationToken);

        if (result.IsSuccess)
        {
            TempData["ListingSuccess"] = SharedResource.Get("ListingDeletion.Succeeded");
            return RedirectToAction("Index");
        }

        if (result.Status == ListingServiceStatus.Forbidden)
        {
            return NotFound();
        }

        var terminal = TerminalFailure(result.Status);

        if (terminal is not null)
        {
            return terminal;
        }

        AddErrors(result.Errors);
        var owned = await listings.GetOwnerListingsAsync(User, cancellationToken);

        if (!owned.IsSuccess)
        {
            return ReadFailure(owned.Status);
        }

        var view = View("Index", new OwnerListingManagementViewModel(owned.Value!));
        view.StatusCode = FailureStatusCode(result.Status);
        return view;
    }
}
