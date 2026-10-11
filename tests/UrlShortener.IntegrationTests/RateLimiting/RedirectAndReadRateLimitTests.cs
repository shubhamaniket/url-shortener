using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests.RateLimiting;

/// <summary>Redirect (burst) and read budgets (feature 003, US2): separate per group and per client.</summary>
public sealed class RedirectAndReadRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task BurstUpToTheBucketSizeThenRejected()
    {
        await using var host = Host(("Redirect:BurstLimit", "5"), ("Redirect:TokensPerSecond", "1"));
        var client = host.CreateClient(NoRedirects);
        var code = await CreateLinkAsync(client, "203.0.113.1");

        var burst = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetAsync("203.0.113.1", $"/{code}")));
        var rejected = await client.GetAsync("203.0.113.1", $"/{code}");

        Assert.All(burst, response => Assert.Equal(HttpStatusCode.Found, response.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.InRange(RetryAfter(rejected), 1, 2);
        Assert.Null(rejected.Headers.Location);
    }

    [Fact]
    public async Task EveryRedirectOutcomeCountsAgainstTheBudget()
    {
        await using var host = Host(("Redirect:BurstLimit", "3"), ("Redirect:TokensPerSecond", "1"));
        var client = host.CreateClient(NoRedirects);
        var code = await CreateLinkAsync(client, "203.0.113.2");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("203.0.113.2", "/zzzzzz1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("203.0.113.2", "/zzzzzz2")).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync("203.0.113.2", $"/{code}")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("203.0.113.2", $"/{code}")).StatusCode);
    }

    [Fact]
    public async Task RejectedRedirectRecordsNoClick()
    {
        await using var host = Host(("Redirect:BurstLimit", "2"), ("Redirect:TokensPerSecond", "1"));
        var client = host.CreateClient(NoRedirects);
        var code = await CreateLinkAsync(client, "203.0.113.3");
        await client.GetAsync("203.0.113.3", $"/{code}");
        await client.GetAsync("203.0.113.3", $"/{code}");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("203.0.113.3", $"/{code}")).StatusCode);

        // Clicks are stored in queue order by one writer: once a later marker click (another client,
        // its own budget) is stored, a wrongly queued click would be stored too.
        var marker = await CreateLinkAsync(client, "203.0.113.4");
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync("203.0.113.4", $"/{marker}")).StatusCode);
        await Eventually.AssertAsync(async () => await ClickCountAsync(host, marker) == 1, "the marker click stored");
        Assert.Equal(2, await ClickCountAsync(host, code));
    }

    [Fact]
    public async Task BudgetsAreSeparatePerGroup()
    {
        await using var host = Host(
            ("Create:PermitLimit", "2"), ("Redirect:BurstLimit", "2"), ("Redirect:TokensPerSecond", "1"), ("Read:PermitLimit", "50"));
        var client = host.CreateClient(NoRedirects);

        // Client A uses up its create budget: following and reading still work.
        var code = await CreateLinkAsync(client, "203.0.113.5");
        await client.CreateLinkAsync("203.0.113.5", new { url = "https://example.com/second" });
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.CreateLinkAsync("203.0.113.5", new { url = "https://example.com/third" })).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync("203.0.113.5", $"/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("203.0.113.5", $"/api/links/{code}")).StatusCode);

        // Client B uses up its redirect budget: creating and reading still work.
        await client.GetAsync("203.0.113.6", $"/{code}");
        await client.GetAsync("203.0.113.6", $"/{code}");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("203.0.113.6", $"/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.CreateLinkAsync("203.0.113.6", new { url = "https://example.com/b" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("203.0.113.6", $"/api/links/{code}/stats")).StatusCode);
    }

    [Fact]
    public async Task ReadBudgetCoversDetailsAndStatisticsTogether()
    {
        await using var host = Host(("Read:PermitLimit", "3"), ("Read:WindowSeconds", "60"));
        var client = host.CreateClient();
        var code = await CreateLinkAsync(client, "203.0.113.7");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("203.0.113.7", $"/api/links/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("203.0.113.7", $"/api/links/{code}/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("203.0.113.7", "/api/links/zzzzzzz")).StatusCode);

        var rejected = await client.GetAsync("203.0.113.7", $"/api/links/{code}/stats");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.InRange(RetryAfter(rejected), 1, 60);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("203.0.113.8", $"/api/links/{code}")).StatusCode);
    }

    [Fact]
    public async Task AnotherClientIsStillRedirected()
    {
        await using var host = Host(("Redirect:BurstLimit", "1"), ("Redirect:TokensPerSecond", "1"));
        var client = host.CreateClient(NoRedirects);
        var code = await CreateLinkAsync(client, "203.0.113.9");
        await client.GetAsync("203.0.113.9", $"/{code}");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("203.0.113.9", $"/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync("203.0.113.10", $"/{code}")).StatusCode);
    }

    [Fact]
    public async Task HealthIsNeverLimited()
    {
        await using var host = Host(("Create:PermitLimit", "1"), ("Redirect:BurstLimit", "1"), ("Read:PermitLimit", "1"));
        var client = host.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => client.GetAsync("203.0.113.11", "/health")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
    }

    private WebApplicationFactory<Program> Host(params (string Setting, string Value)[] budgets) =>
        RateLimitedHost.Create(factory, budgets.ToDictionary<(string Setting, string Value), string, string?>(b => $"RateLimiting:{b.Setting}", b => b.Value));

    private static int RetryAfter(HttpResponseMessage response) =>
        int.Parse(Assert.Single(response.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture);

    private static async Task<string> CreateLinkAsync(HttpClient client, string clientIp)
    {
        var response = await client.CreateLinkAsync(clientIp, new { url = $"https://example.com/{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
    }

    private static async Task<long> ClickCountAsync(WebApplicationFactory<Program> host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks
            .Where(link => link.Code == code).Select(link => link.ClickCount).SingleAsync();
    }
}