using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RentRoad.Controllers;

/// <summary>
/// Provides the authenticated placeholder for future account settings.
/// </summary>
[Authorize]
[Route("settings")]
public sealed class SettingsController : Controller
{
    /// <summary>
    /// Displays the coming-soon page without changing account information.
    /// </summary>
    /// <returns>The MVC response for the operation's outcome.</returns>
    [HttpGet("")]
    public IActionResult Index() => View();
}
