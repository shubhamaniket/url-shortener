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

    [Fact]
    public async Task ConcurrentCreatesWithSameAliasProduceExactlyOneLink()
    {
        const int Attempts = 10;
        var alias = $"race-{Guid.NewGuid():N}"[..20];
        var client = factory.CreateClient();

        // Each request asks for the same alias in a different letter case, all in flight at once.
        var responses = await Task.WhenAll(Enumerable.Range(0, Attempts)
            .Select(i => client.PostAsJsonAsync(
                new Uri("/api/links", UriKind.Relative),
                new { url = $"https://example.com/race/{i}", customAlias = i % 2 == 0 ? alias : alias.ToUpperInvariant() })));

        var statuses = responses.Select(response => response.StatusCode).ToList();
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(Attempts - 1, statuses.Count(status => status == HttpStatusCode.Conflict));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses);

        await using var scope = factory.Services.CreateAsyncScope();
        var normalized = alias.ToLowerInvariant();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks
            .CountAsync(link => link.NormalizedCode == normalized));
    }

    private async Task<string> CreateLinkAsync(string url)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(new Uri("/api/links", UriKind.Relative), new { url });
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;
    }
}