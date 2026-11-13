using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using RentRoad.Services.Messaging;
using RentRoad.ViewModels.Messaging;

namespace RentRoad.Controllers;

/// <summary>
/// Marks messaging HTTP APIs for cookie 401/403 responses and normalizes MVC's anti-forgery/content-type errors
/// locally.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MessagingJsonAttribute : Attribute, IDisableCookieRedirectMetadata, IAsyncAlwaysRunResultFilter
{
    /// <summary>
    /// Uses the messaging JSON envelope for framework rejections without returning attempted values or
    /// technical diagnostics.
    /// </summary>
    /// <param name="context">The MVC result-filter context containing the pending response.</param>
    /// <param name="next">The delegate that executes the remaining result filters and response.</param>
    /// <returns>A task representing completion of result-filter execution.</returns>
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";

        if (context.Result is not JsonResult
            && context.Result is IStatusCodeActionResult { StatusCode: >= 400 } rejected)
        {
            var key = context.Result is AntiforgeryValidationFailedResult
                ? "Messages.InvalidAntiforgeryToken"
                : rejected.StatusCode == StatusCodes.Status415UnsupportedMediaType
                    ? "Messages.UnsupportedContentType"
                    : "Validation.NonPropertyUnknownInvalid";

            context.Result = new JsonResult(
                new MessagingResponseViewModel<object>(
                    false,
                    MessagingServiceStatus.ValidationFailed.ToString(),
                    null,
                    false,
                    false,
                    new[] { new MessagingErrorViewModel(string.Empty, key, SharedResource.Get(key)) }))
            {
                StatusCode = rejected.StatusCode
            };
        }

        await next();
    }
}
