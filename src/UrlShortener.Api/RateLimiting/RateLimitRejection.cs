using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace UrlShortener.Api.RateLimiting;

/// <summary>
/// Answers a rejected request with 429, <c>Retry-After</c> and a ProblemDetails body like every other
/// error, and logs the rejection by policy and route pattern, never by client (FR-005, FR-012).
/// </summary>
internal static partial class RateLimitRejection
{
    public static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var endpoint = http.GetEndpoint();
        var policy = endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "unknown";

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var fromLimiter)
            ? fromLimiter
            : Fallback(http, policy);
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        // The route pattern (e.g. /{code}), not the path: no client data and no code values in logs.
        var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? endpoint?.DisplayName ?? "unknown";
        LogRejected(http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RateLimitRejection)), policy, route);

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails =
            {
                Status = StatusCodes.Status429TooManyRequests,
                Type = "https://tools.ietf.org/html/rfc6585#section-4",
                Title = "Too many requests",
                Detail = $"Request budget for this endpoint exceeded. Retry after {seconds} seconds.",
            },
        });
    }

    /// <summary>Used only if a limiter gives no retry time: the policy's window.</summary>
    private static TimeSpan Fallback(HttpContext http, string policy)
    {
        var budgets = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        return policy switch
        {
            RateLimitPolicies.Create => TimeSpan.FromSeconds(budgets.Create.WindowSeconds),
            RateLimitPolicies.Read => TimeSpan.FromSeconds(budgets.Read.WindowSeconds),
            _ => TimeSpan.FromSeconds(1), // the token bucket refills every second
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit exceeded for policy {Policy} on {Endpoint}")]
    private static partial void LogRejected(ILogger logger, string policy, string endpoint);
}