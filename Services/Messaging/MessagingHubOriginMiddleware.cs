using RentRoad.Hubs;

namespace RentRoad.Services.Messaging;

/// <summary>Rejects cross-origin browser hub traffic, including WebSocket upgrades, without enabling credentialed cross-origin access.</summary>
public sealed class MessagingHubOriginMiddleware
{
    private readonly RequestDelegate next;

    /// <summary>Initializes the narrowly scoped hub-origin guard.</summary>
    public MessagingHubOriginMiddleware(RequestDelegate next) => this.next = next;

    /// <summary>Requires any supplied Origin to match the request's scheme, host and effective port; native clients without Origin still authenticate normally.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(MessagesHub.ROUTE) && context.Request.Headers.TryGetValue("Origin", out var origins)
            && (origins.Count != 1 || !IsSameOrigin(context.Request, origins[0])))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        return next(context);
    }

    /// <summary>Compares normalized browser origin components without trusting paths, user information or forwarded headers.</summary>
    private static bool IsSameOrigin(HttpRequest request, string? value) => Uri.TryCreate(value, UriKind.Absolute, out var origin)
        && Uri.TryCreate(request.Scheme + "://" + request.Host.ToUriComponent(), UriKind.Absolute, out var target)
        && origin.Scheme is "http" or "https" && origin.AbsolutePath == "/" && origin.UserInfo.Length == 0
        && origin.Query.Length == 0 && origin.Fragment.Length == 0
        && string.Equals(origin.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(origin.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase) && origin.Port == target.Port;
}
