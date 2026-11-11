using System.Globalization;
using System.Resources;

namespace RentRoad;

/// <summary>Identifies the shared resources for dependency injection and existing static presentation helpers.</summary>
public sealed class SharedResource
{
    private static readonly ResourceManager resources = new("RentRoad.Resources.SharedResource", typeof(SharedResource).Assembly);

    /// <summary>Provides resource-backed metadata for direct and MVC annotation validation.</summary>
    public static string FirstDayLabel => Get("Availability.FirstDay");

    /// <summary>Provides resource-backed metadata for direct and MVC annotation validation.</summary>
    public static string LastDayLabel => Get("Availability.LastDay");

    /// <summary>Provides resource-backed metadata for direct and MVC annotation validation.</summary>
    public static string FirstDayRequired => Get("Availability.FirstDayRequired");

    /// <summary>Provides resource-backed metadata for direct and MVC annotation validation.</summary>
    public static string LastDayRequired => Get("Availability.LastDayRequired");

    /// <summary>Provides resource-backed metadata for direct and MVC annotation validation.</summary>
    public static string ImageFileRequired => Get("ListingImages.FileRequired");

    /// <summary>Provides resource-backed metadata for direct and MVC annotation validation.</summary>
    public static string ImageOrderRequired => Get("ListingImages.OrderRequired");

    /// <summary>Provides standard resource-backed account validation metadata.</summary>
    public static string RegisterDisplayName => Get("Identity.DisplayName");

    /// <summary>Provides standard resource-backed account validation metadata.</summary>
    public static string RegisterEmail => Get("Identity.Email");

    /// <summary>Provides standard resource-backed account validation metadata.</summary>
    public static string RegisterPassword => Get("Identity.Password");

    /// <summary>Provides standard resource-backed account validation metadata.</summary>
    public static string RegisterConfirmPassword => Get("Identity.ConfirmPassword");

    /// <summary>Provides standard resource-backed account validation metadata.</summary>
    public static string RequiredField => Get("Validation.Required");

    /// <summary>Provides standard resource-backed account validation metadata.</summary>
    public static string MaximumLength => Get("Validation.MaximumLength");

    /// <summary>Provides standard resource-backed account validation metadata.</summary>
    public static string PasswordLength => Get("Identity.PasswordLength");

    /// <summary>Provides standard resource-backed account validation metadata.</summary>
    public static string EmailAddressInvalid => Get("Identity.EmailAddressInvalid");

    /// <summary>Provides standard resource-backed account validation metadata.</summary>
    public static string PasswordsDiffer => Get("Identity.PasswordsDiffer");

    /// <summary>Resolves presentation text in the request UI culture, preserving a detectable key fallback.</summary>
    public static string Get(string key, params object[] arguments)
    {
        var value = resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return arguments.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }
}
