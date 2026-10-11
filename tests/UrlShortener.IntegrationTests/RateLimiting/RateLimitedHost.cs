using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UrlShortener.IntegrationTests.RateLimiting;

/// <summary>Builds a test host with small, explicit budgets and per-request client addresses.</summary>
internal static class RateLimitedHost
{
    public static WebApplicationFactory<Program> Create(ApiFactory factory, IDictionary<string, string?> budgets) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(budgets));
            builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, ClientAddressStartupFilter>());
        });

    public static Task<HttpResponseMessage> CreateLinkAsync(this HttpClient client, string clientIp, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/links", UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Add(ClientAddressStartupFilter.Header, clientIp);
        return client.SendAsync(request);
    }
}