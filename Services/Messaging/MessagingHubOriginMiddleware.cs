using RentRoad.Hubs;

namespace RentRoad.Services.Messaging;

/// <summary>
/// Rejects cross-origin browser hub traffic, including WebSocket upgrades, without enabling credentialed
/// cross-origin access.
/// </summary>
public sealed class MessagingHubOriginMiddleware
{
    private readonly RequestDelegate next;

    /// <summary>
    /// Initializes the narrowly scoped hub-origin guard.
    /// </summary>
    /// <param name="next">The next middleware delegate in the request pipeline.</param>
    public MessagingHubOriginMiddleware(RequestDelegate next) => this.next = next;

    /// <summary>
    /// Requires any supplied Origin to match the request's scheme, host and effective port; native clients without
    /// Origin still authenticate normally.
    /// </summary>
    /// <param name="context">The HTTP context for the hub request.</param>
    /// <returns>A task representing completion of the origin check and any subsequent middleware.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(MessagesHub.ROUTE)
            && context.Request.Headers.TryGetValue("Origin", out var origins)
            && (origins.Count != 1 || !IsSameOrigin(context.Request, origins[0])))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        return next(context);
    }

    /// <summary>
    /// Compares normalized browser origin components without trusting paths, user information or forwarded headers.
    /// </summary>
    /// <param name="request">The HTTP request whose origin is checked.</param>
    /// <param name="value">The supplied Origin header value.</param>
    /// <returns>Whether the supplied origin matches the request's scheme, host and port.</returns>
    private static bool IsSameOrigin(HttpRequest request, string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var origin)
            && Uri.TryCreate(request.Scheme + "://" + request.Host.ToUriComponent(), UriKind.Absolute, out var target)
            && origin.Scheme is "http" or "https"
            && origin.AbsolutePath == "/"
            && origin.UserInfo.Length == 0
            && origin.Query.Length == 0
            && origin.Fragment.Length == 0
            && string.Equals(origin.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(origin.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase)
            && origin.Port == target.Port;
}
