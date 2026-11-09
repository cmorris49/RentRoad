using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Models;
using RentRoad.ViewModels.Listings;

namespace RentRoad.Controllers;

public class HomeController : Controller
{
    /// <summary>
    /// Displays the marketplace introduction with the existing public search input contract.
    /// </summary>
    public IActionResult Index()
    {
        return View(new ListingSearchInputViewModel());
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
