using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Models;
using RentRoad.Services.Listings;
using RentRoad.ViewModels.Listings;

namespace RentRoad.Controllers;

/// <summary>
/// Presents the marketplace home, privacy and error pages.
/// </summary>
public class HomeController : Controller
{
    private readonly IListingService listings;

    /// <summary>
    /// Initializes Home with the existing public listing service.
    /// </summary>
    /// <param name="listings">The service used to retrieve and manage listing information.</param>
    public HomeController(IListingService listings)
    {
        this.listings = listings;
    }

    /// <summary>
    /// Displays the marketplace introduction with the existing public search input contract.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task containing the MVC response for the operation.</returns>
    public async Task<IActionResult> IndexAsync(CancellationToken cancellationToken = default)
    {
        var popular = await listings.GetPopularListingsAsync(cancellationToken);

        return View(
            "Index",
            new HomeViewModel
            {
                PopularListings = popular.Select(ListingSummaryViewModel.FromResult).ToArray()
            });
    }

    /// <summary>
    /// Displays the site's privacy information.
    /// </summary>
    /// <returns>The MVC response for the operation's outcome.</returns>
    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>
    /// Renders a privacy-safe missing-page response without changing its HTTP status.
    /// </summary>
    /// <returns>The branded missing-page view with status code 404.</returns>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult PageNotFound()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;

        return View();
    }

    /// <summary>
    /// Displays a safe error page with a diagnostic request identifier.
    /// </summary>
    /// <returns>The MVC response for the operation's outcome.</returns>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(
            new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
    }
}
