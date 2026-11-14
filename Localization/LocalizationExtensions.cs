using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;

namespace RentRoad.Localization;

/// <summary>
/// Shares the production localization setup with isolated MVC hosts.
/// </summary>
public static class LocalizationExtensions
{
    /// <summary>
    /// Registers English resources, localized validation and request-culture options without storing
    /// language preferences.
    /// </summary>
    /// <param name="mvc">The MVC builder whose services and localization options are configured.</param>
    /// <returns>The MVC builder with the localization registrations added.</returns>
    public static IMvcBuilder AddRentRoadLocalization(this IMvcBuilder mvc)
    {
        mvc.Services.AddScoped<Microsoft.AspNetCore.Identity.IdentityErrorDescriber, LocalizedIdentityErrorDescriber>();
        mvc.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        mvc.Services.Configure<RequestLocalizationOptions>(options => options
            .SetDefaultCulture("en")
            .AddSupportedCultures("en")
            .AddSupportedUICultures("en"));

        mvc.AddViewLocalization();
        mvc.AddDataAnnotationsLocalization(options => options.DataAnnotationLocalizerProvider =
            (_, factory) => factory.Create(typeof(SharedResource)));
        mvc.Services
            .AddOptions<Microsoft.AspNetCore.Mvc.MvcOptions>()
            .Configure<IStringLocalizer<SharedResource>>((options, text) =>
            {
                options.ModelBindingMessageProvider.SetMissingBindRequiredValueAccessor(
                    field => text["Validation.MissingBindRequired", field]);
                options.ModelBindingMessageProvider.SetMissingKeyOrValueAccessor(
                    () => text["Validation.MissingKeyOrValue"]);
                options.ModelBindingMessageProvider.SetMissingRequestBodyRequiredValueAccessor(
                    () => text["Validation.MissingBody"]);
                options.ModelBindingMessageProvider.SetValueIsInvalidAccessor(
                    value => text["Validation.NullValue", value]);
                options.ModelBindingMessageProvider.SetNonPropertyAttemptedValueIsInvalidAccessor(
                    value => text["Validation.NonPropertyAttemptInvalid", value]);
                options.ModelBindingMessageProvider.SetUnknownValueIsInvalidAccessor(
                    field => text["Validation.UnknownValueInvalid", field]);
                options.ModelBindingMessageProvider.SetNonPropertyUnknownValueIsInvalidAccessor(
                    () => text["Validation.NonPropertyUnknownInvalid"]);
                options.ModelBindingMessageProvider.SetNonPropertyValueMustBeANumberAccessor(
                    () => text["Validation.NonPropertyNumber"]);
                options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor(
                    (value, field) => text["Validation.InvalidValue", value, field]);
                options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(
                    field => text["Validation.NumberRequired", field]);
                options.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(
                    value => text["Validation.NullValue", value]);
            });

        return mvc;
    }
}
