using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace RentRoad.Services.Reviews;

/// <summary>
/// Registers the configurable review-only provider, cache coordinator and owner POST rate policy.
/// </summary>
public static class ReviewSummaryServiceCollectionExtensions
{
    public const string GENERATION_POLICY = "review-summary-generation";

    /// <summary>
    /// Adds generation dependencies without requiring a key or making a provider request at startup.
    /// </summary>
    /// <param name="services">The application dependency registrations.</param>
    /// <param name="configuration">The server-side secure configuration.</param>
    /// <returns>The service collection for further registrations.</returns>
    public static IServiceCollection AddReviewSummaries(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GeminiReviewSummaryOptions>(configuration.GetSection("Gemini"));
        services.AddSingleton<ReviewSummaryGenerationGate>();
        services.AddScoped<IReviewSummaryService, ReviewSummaryService>();
        services.AddHttpClient<IGeminiReviewSummarizer, GeminiReviewSummarizer>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(GENERATION_POLICY, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 3,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });
        return services;
    }
}
