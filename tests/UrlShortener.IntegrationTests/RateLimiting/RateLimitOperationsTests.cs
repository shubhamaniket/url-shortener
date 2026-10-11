using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace UrlShortener.IntegrationTests.RateLimiting;

/// <summary>Operator-facing behaviour (feature 003, US3): configured budgets, startup validation, logs.</summary>
public sealed class RateLimitOperationsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ConfiguredBudgetIsEnforced()
    {
        await using var host = RateLimitedHost.Create(factory, new Dictionary<string, string?> { ["RateLimiting:Create:PermitLimit"] = "3" });
        var client = host.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.CreateLinkAsync("192.0.2.1", new { url = $"https://example.com/ops/{i}" })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
    }

    [Theory]
    [InlineData("RateLimiting:Read:WindowSeconds", "0")]
    [InlineData("RateLimiting:Redirect:TokensPerSecond", "-1")]
    [InlineData("RateLimiting:Create:PermitLimit", "0")]
    public void InvalidBudgetStopsStartupNamingTheSetting(string setting, string value)
    {
        using var invalid = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { [setting] = value })));

        var exception = Record.Exception(() => invalid.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains($"{setting} must be at least 1.", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectionIsLoggedByPolicyWithoutTheClientAddress()
    {
        const string ClientIp = "192.0.2.123";
        using var logs = new CapturingLoggerProvider();
        await using var host = RateLimitedHost.Create(factory, new Dictionary<string, string?> { ["RateLimiting:Create:PermitLimit"] = "1" })
            .WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        var client = host.CreateClient();

        await client.CreateLinkAsync(ClientIp, new { url = "https://example.com/first" });
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.CreateLinkAsync(ClientIp, new { url = "https://example.com/second" })).StatusCode);

        var rejection = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Rate limit exceeded", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, rejection.Level);
        Assert.Equal("Rate limit exceeded for policy create on api/links", rejection.Message);
        Assert.DoesNotContain(logs.Entries, entry => entry.FullText.Contains(ClientIp, StringComparison.Ordinal));
    }

    private static string Flatten(Exception exception) =>
        exception is AggregateException aggregate
            ? string.Join(" | ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
            : exception.ToString();
}