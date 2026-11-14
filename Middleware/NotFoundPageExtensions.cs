using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Net.Http.Headers;

namespace RentRoad.Middleware;

/// <summary>
/// Adds HTML missing-page handling while retaining API responses and other HTTP failures.
/// </summary>
public static class NotFoundPageExtensions
{
    /// <summary>
    /// Re-executes only empty, navigational 404 responses through the public missing-page view.
    /// </summary>
    /// <param name="app">The pipeline configured before routing.</param>
    /// <returns>The application builder with missing-page handling registered.</returns>
    public static IApplicationBuilder UseRentRoadNotFoundPages(this IApplicationBuilder app)
    {
        return app.UseWhen(IsHtmlNavigation, branch =>
        {
            branch.UseStatusCodePagesWithReExecute("/Home/PageNotFound");
            branch.Use(async (context, next) =>
            {
                await next();

                if (context.Response.StatusCode != StatusCodes.Status404NotFound)
                {
                    var feature = context.Features.Get<IStatusCodePagesFeature>();

                    if (feature is not null)
                    {
                        feature.Enabled = false;
                    }
                }
            });
        });
    }

    /// <summary>
    /// Limits HTML status pages to document reads without intercepting JSON or SignalR requests.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <returns>Whether the request can receive a navigational HTML missing-page response.</returns>
    private static bool IsHtmlNavigation(HttpContext context)
    {
        if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            || context.Request.Path.StartsWithSegments("/hubs/messages"))
        {
            return false;
        }

        var accept = context.Request.Headers.Accept;

        if (accept.Count == 0)
        {
            return true;
        }

        if (!MediaTypeHeaderValue.TryParseList(accept.Select(value => value ?? string.Empty).ToArray(), out var types))
        {
            return false;
        }

        return !types.Any(type => type.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            && types.Any(type => type.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                && type.Quality != 0);
    }
}
