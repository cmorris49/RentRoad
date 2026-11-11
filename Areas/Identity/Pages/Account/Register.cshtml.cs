using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using RentRoad.Models;

namespace RentRoad.Areas.Identity.Pages.Account;

/// <summary>
/// Registers an Identity account with the required RentRoad display name.
/// </summary>
[AllowAnonymous]
public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> userManager;
    private readonly SignInManager<ApplicationUser> signInManager;
    private readonly IEmailSender<ApplicationUser> emailSender;
    private readonly ILogger<RegisterModel> logger;

    /// <summary>
    /// Initializes the registration page with the existing Identity services.
    /// </summary>
    /// <param name="userManager">The Identity account manager.</param>
    /// <param name="signInManager">The Identity sign-in manager.</param>
    /// <param name="emailSender">The Identity confirmation email sender.</param>
    /// <param name="logger">The registration logger.</param>
    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailSender<ApplicationUser> emailSender,
        ILogger<RegisterModel> logger)
    {
        this.userManager = userManager;
        this.signInManager = signInManager;
        this.emailSender = emailSender;
        this.logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; private set; }

    public IList<AuthenticationScheme> ExternalLogins { get; private set; } = new List<AuthenticationScheme>();

    /// <summary>
    /// Contains the account details accepted by the registration form.
    /// </summary>
    public class InputModel
    {
        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.RequiredField))]
        [StringLength(100, ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.MaximumLength))]
        [Display(Name = nameof(SharedResource.RegisterDisplayName), ResourceType = typeof(SharedResource))]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.RequiredField))]
        [EmailAddress(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.EmailAddressInvalid))]
        [Display(Name = nameof(SharedResource.RegisterEmail), ResourceType = typeof(SharedResource))]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.RequiredField))]
        [StringLength(100, MinimumLength = 6, ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.PasswordLength))]
        [DataType(DataType.Password)]
        [Display(Name = nameof(SharedResource.RegisterPassword), ResourceType = typeof(SharedResource))]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = nameof(SharedResource.RegisterConfirmPassword), ResourceType = typeof(SharedResource))]
        [Compare(nameof(Password), ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.PasswordsDiffer))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Loads the registration form and available external sign-in providers.
    /// </summary>
    /// <param name="returnUrl">The local destination after registration.</param>
    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
    }

    /// <summary>
    /// Creates an account and preserves Identity email confirmation and sign-in behaviour.
    /// </summary>
    /// <param name="returnUrl">The local destination after registration.</param>
    /// <returns>The form with errors, confirmation page, or local destination.</returns>
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");
        ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = new ApplicationUser
        {
            DisplayName = Input.DisplayName.Trim(),
            UserName = Input.Email,
            Email = Input.Email
        };
        var result = await userManager.CreateAsync(user, Input.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        logger.LogInformation("A user created a new account with a password.");
        await SendConfirmationEmailAsync(user);

        if (userManager.Options.SignIn.RequireConfirmedAccount)
        {
            return RedirectToPage("./RegisterConfirmation", new { email = Input.Email, returnUrl = ReturnUrl });
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(ReturnUrl);
    }

    /// <summary>
    /// Sends the standard Identity email confirmation link for a new account.
    /// </summary>
    /// <param name="user">The newly registered account.</param>
    private async Task SendConfirmationEmailAsync(ApplicationUser user)
    {
        var userId = await userManager.GetUserIdAsync(user);
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var confirmationUrl = Url.Page(
            "/Account/ConfirmEmail",
            pageHandler: null,
            values: new { area = "Identity", userId, code = encodedToken, returnUrl = ReturnUrl },
            protocol: Request.Scheme)
            ?? throw new InvalidOperationException("Unable to create the email confirmation link.");

        await emailSender.SendConfirmationLinkAsync(user, Input.Email, HtmlEncoder.Default.Encode(confirmationUrl));
    }
}
