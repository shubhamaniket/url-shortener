using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests.RateLimiting;

/// <summary>The create budget end to end (feature 003, US1): 429 + Retry-After + ProblemDetails, nothing created.</summary>
public sealed class CreateRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Dictionary<string, string?> TenPerMinute = new()
    {
        ["RateLimiting:Create:PermitLimit"] = "10",
        ["RateLimiting:Create:WindowSeconds"] = "60",
    };

    [Fact]
    public async Task EleventhCreateInAMinuteIsRejectedAndCreatesNothing()
    {
        await using var host = RateLimitedHost.Create(factory, TenPerMinute);
        var client = host.CreateClient();
        var before = await LinkCountAsync(host);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.CreateLinkAsync("198.51.100.1", Link(i))).StatusCode);
        }

        var rejected = await client.CreateLinkAsync("198.51.100.1", Link(10));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        var retryAfter = int.Parse(Assert.Single(rejected.Headers.GetValues("Retry-After")), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 60);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Equal("Too many requests", problem.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc6585#section-4", problem.GetProperty("type").GetString());
        Assert.Contains($"{retryAfter} seconds", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.Equal(before + 10, await LinkCountAsync(host));
    }

    [Fact]
    public async Task InvalidCreatesCountAgainstTheBudget()
    {
        await using var host = RateLimitedHost.Create(factory, TenPerMinute);
        var client = host.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.CreateLinkAsync("198.51.100.2", new { url = "javascript:alert(1)" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.CreateLinkAsync("198.51.100.2", Link(0))).StatusCode);
    }

    [Fact]
    public async Task AnotherClientIsUnaffected()
    {
        await using var host = RateLimitedHost.Create(factory, TenPerMinute);
        var client = host.CreateClient();
        for (var i = 0; i < 11; i++)
        {
            await client.CreateLinkAsync("198.51.100.3", Link(i));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.CreateLinkAsync("198.51.100.3", Link(99))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.CreateLinkAsync("198.51.100.4", Link(99))).StatusCode);
    }

    [Fact]
    public async Task ForwardedForHeaderDoesNotEscapeTheBudget()
    {
        await using var host = RateLimitedHost.Create(factory, TenPerMinute);
        var client = host.CreateClient();
        for (var i = 0; i < 10; i++)
        {
            await client.CreateLinkAsync("198.51.100.5", Link(i));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/links", UriKind.Relative)) { Content = JsonContent.Create(Link(10)) };
        request.Headers.Add(ClientAddressStartupFilter.Header, "198.51.100.5");
        request.Headers.Add("X-Forwarded-For", "203.0.113.77");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task ClientSucceedsAgainAfterRetryAfter()
    {
        await using var host = RateLimitedHost.Create(factory, new Dictionary<string, string?>
        {
            ["RateLimiting:Create:PermitLimit"] = "2",
            ["RateLimiting:Create:WindowSeconds"] = "1",
        });
        var client = host.CreateClient();
        await client.CreateLinkAsync("198.51.100.6", Link(0));
        await client.CreateLinkAsync("198.51.100.6", Link(1));

        var rejected = await client.CreateLinkAsync("198.51.100.6", Link(2));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = int.Parse(Assert.Single(rejected.Headers.GetValues("Retry-After")), System.Globalization.CultureInfo.InvariantCulture);

        await Task.Delay(TimeSpan.FromSeconds(retryAfter) + TimeSpan.FromMilliseconds(100));

        Assert.Equal(HttpStatusCode.Created, (await client.CreateLinkAsync("198.51.100.6", Link(3))).StatusCode);
    }

    private static object Link(int i) => new { url = $"https://example.com/rate/{i}" };

    private static async Task<int> LinkCountAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks.CountAsync();
    }
}