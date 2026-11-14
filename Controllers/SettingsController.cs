using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RentRoad.Controllers;

/// <summary>
/// Provides authenticated preferences and entry points into the existing Identity account pages.
/// </summary>
[Authorize]
[Route("settings")]
public sealed class SettingsController : Controller
{
    /// <summary>
    /// Displays one supported preference or opens its existing Identity management page.
    /// </summary>
    /// <param name="section">The allowlisted settings section, or null for the settings overview.</param>
    /// <returns>The selected settings view, an Identity page redirect, or a missing-section response.</returns>
    [HttpGet("")]
    public IActionResult Index(string? section = null)
    {
        var page = section switch
        {
            "account" => "/Account/Manage/Index",
            "email" => "/Account/Manage/Email",
            "password" => "/Account/Manage/ChangePassword",
            "security" => "/Account/Manage/TwoFactorAuthentication",
            "personal-data" => "/Account/Manage/PersonalData",
            "external-logins" => "/Account/Manage/ExternalLogins",
            _ => null
        };

        if (page is not null)
        {
            return RedirectToPage(page, new { area = "Identity" });
        }

        if (section is not null and not "theme" and not "appearance" and not "language")
        {
            return NotFound();
        }

        ViewData["SettingsOverview"] = section is null;
        ViewData["SettingsSection"] = section == "language" ? "language" : "theme";

        return View();
    }
}
