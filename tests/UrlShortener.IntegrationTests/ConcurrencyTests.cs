using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

/// <summary>Races real requests against the real SQLite database.</summary>
public sealed class ConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int ParallelRequests = 20;

    [Fact]
    public async Task ConcurrentRedirectsAreAllCounted()
    {
        var code = await CreateLinkAsync("https://example.com/popular");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Start every request before awaiting any, so the click updates overlap.
        var responses = await Task.WhenAll(Enumerable.Range(0, ParallelRequests)
            .Select(_ => client.GetAsync(new Uri($"/{code}", UriKind.Relative))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Found, response.StatusCode));

        await using var scope = factory.Services.CreateAsyncScope();
        var clickCount = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks
            .Where(link => link.Code == code)
            .Select(link => link.ClickCount)
            .SingleAsync();
        Assert.Equal(ParallelRequests, clickCount);
    }

    private async Task<string> CreateLinkAsync(string url)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(new Uri("/api/links", UriKind.Relative), new { url });
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;
    }
}