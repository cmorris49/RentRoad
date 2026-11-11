using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Models;
using RentRoad.ViewModels.Account;

namespace RentRoad.ViewComponents;

/// <summary>Loads the current Identity display label outside Razor without exposing private account fields.</summary>
public sealed class AccountNavigationViewComponent : ViewComponent
{
    private readonly UserManager<ApplicationUser> users;

    /// <summary>Initializes account navigation with the application's existing Identity user manager.</summary>
    public AccountNavigationViewComponent(UserManager<ApplicationUser> users) => this.users = users;

    /// <summary>Shows the saved display name or a non-email username, preserving anonymous Identity links.</summary>
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var signedIn = HttpContext.User.Identity?.IsAuthenticated == true;
        var user = signedIn ? await users.GetUserAsync(HttpContext.User) : null;
        var name = user?.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            var username = user?.UserName?.Trim();
            name = !string.IsNullOrWhiteSpace(username) && !username.Contains('@') ? username : "Account";
        }
        return View(new AccountNavigationViewModel(signedIn, name));
    }
}
