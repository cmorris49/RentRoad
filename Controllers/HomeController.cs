using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Models;
using RentRoad.Services.Listings;
using RentRoad.ViewModels.Listings;

namespace RentRoad.Controllers;

public class HomeController : Controller
{
    private readonly IListingService listings;

    /// <summary>
    /// Initializes Home with the existing public listing service.
    /// </summary>
    public HomeController(IListingService listings)
    {
        this.listings = listings;
    }

    /// <summary>
    /// Displays the marketplace introduction with the existing public search input contract.
    /// </summary>
    public async Task<IActionResult> IndexAsync(CancellationToken cancellationToken = default)
    {
        var popular = await listings.GetPopularListingsAsync(cancellationToken);
        return View("Index", new HomeViewModel { PopularListings = popular.Select(ListingSummaryViewModel.FromResult).ToArray() });
    }

    /// <summary>
    /// Displays the site's privacy information.
    /// </summary>
    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>
    /// Displays a safe error page with a diagnostic request identifier.
    /// </summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
