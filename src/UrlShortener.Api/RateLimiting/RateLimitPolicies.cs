using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace UrlShortener.Api.RateLimiting;

/// <summary>
/// Named per-client rate-limit policies, attached to endpoints with <c>[EnableRateLimiting]</c>
/// (feature 003, research R1–R3). There is no global limiter: endpoints without the attribute, such as
/// <c>/health</c>, are never limited (FR-009).
/// </summary>
public static class RateLimitPolicies
{
    public const string Create = "create";

    public const string Redirect = "redirect";

    public const string Read = "read";

    /// <summary>The partition used when a request has no client address (FR-011).</summary>
    internal const string UnknownClient = "unknown";

    public static IServiceCollection AddUrlShortenerRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>((limiter, settings) =>
        {
            var budgets = settings.Value;
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = RateLimitRejection.OnRejectedAsync;

            limiter.AddPolicy(Create, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => FixedWindow(budgets.Create)));

            // Bursts (a link shared in a busy chat) up to the bucket size, refilled every second so a
            // burst never means a minute-long lockout (research R2).
            limiter.AddPolicy(Redirect, context => RateLimitPartition.GetTokenBucketLimiter(
                ClientKey(context),
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = budgets.Redirect.BurstLimit,
                    TokensPerPeriod = budgets.Redirect.TokensPerSecond,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            limiter.AddPolicy(Read, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => FixedWindow(budgets.Read)));
        });

        return services;
    }

    /// <summary>
    /// The connection's own address, in memory only: never stored or logged (FR-011). Addresses claimed
    /// in headers such as <c>X-Forwarded-For</c> are deliberately ignored; trusting them would let any
    /// client escape its budget (FR-011a, Clarification 2).
    /// </summary>
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;

    // Queue limit 0: excess requests are rejected at once, never delayed (FR-005).
    private static FixedWindowRateLimiterOptions FixedWindow(FixedWindowBudget budget) => new()
    {
        PermitLimit = budget.PermitLimit,
        Window = TimeSpan.FromSeconds(budget.WindowSeconds),
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}