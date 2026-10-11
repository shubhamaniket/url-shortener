using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace UrlShortener.IntegrationTests.RateLimiting;

/// <summary>
/// Test-only: the test server leaves the connection's remote address empty, so every request would
/// look like the same client. This sets it from the <see cref="Header"/> header before anything else
/// runs. Production code never reads this header (FR-011a).
/// </summary>
internal sealed class ClientAddressStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Client-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (IPAddress.TryParse(context.Request.Headers[Header], out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}