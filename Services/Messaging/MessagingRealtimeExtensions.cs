using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using RentRoad.Hubs;

namespace RentRoad.Services.Messaging;

/// <summary>
/// Shares the production SignalR registration and authenticated route with isolated integration hosts.
/// </summary>
public static class MessagingRealtimeExtensions
{
    /// <summary>
    /// Registers the built-in server, bounded subscription invocations and a scoped post-commit notifier without
    /// adding a client or managed service.
    /// </summary>
    /// <param name="services">The application service collection to configure.</param>
    /// <returns>The service collection with the registrations added.</returns>
    public static IServiceCollection AddMessagingRealtime(this IServiceCollection services)
    {
        services.AddSignalR().AddHubOptions<MessagesHub>(options =>
            {
                options.EnableDetailedErrors = false;
                options.MaximumReceiveMessageSize = MessagesHub.MAX_RECEIVE_BYTES;
                options.MaximumParallelInvocationsPerClient = 1;
                options.HandshakeTimeout = TimeSpan.FromSeconds(10);
            });
        services.AddScoped<IMessageRealtimeNotifier, SignalRMessageRealtimeNotifier>();
        return services;
    }

    /// <summary>
    /// Maps the Identity-authorized hub and closes live connections when their authentication ticket expires.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder used to map the messaging hub.</param>
    /// <returns>The convention builder for the authorized messaging hub endpoint.</returns>
    public static HubEndpointConventionBuilder MapMessagesHub(this IEndpointRouteBuilder endpoints) =>
        endpoints
            .MapHub<MessagesHub>(MessagesHub.ROUTE, options => options.CloseOnAuthenticationExpiration = true)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = IdentityConstants.ApplicationScheme });
}
