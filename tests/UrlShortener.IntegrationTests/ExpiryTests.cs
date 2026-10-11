using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Expiry end to end (feature 002, US1): create with a zone, 410 once expired, details still readable.
/// Tests move the shared clock forward only and set every expiry relative to it, so they stay
/// independent in any order.
/// </summary>
public sealed class ExpiryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri LinksUri = new("/api/links", UriKind.Relative);
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task ExpiryWithOffsetIsReturnedInUtc()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            LinksUri, new { url = "https://example.com/sale", expiresAt = "2099-12-31T23:59:00+05:30" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("2099-12-31T18:29:00Z", (await ReadJsonAsync(response)).GetProperty("expiresAtUtc").GetString());
    }

    [Theory]
    [InlineData("2099-12-31T23:59:00", "The expiry needs a time zone, e.g. 2026-12-31T23:59:00Z or 2026-12-31T23:59:00+05:30.")]
    [InlineData("2020-01-01T00:00:00Z", "The expiry must be in the future.")]
    [InlineData("tomorrow", "The expiry must be a date and time with a time zone, e.g. 2026-12-31T23:59:00+05:30.")]
    public async Task InvalidExpiryReturns400KeyedByExpiresAt(string expiresAt, string message)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            LinksUri, new { url = "https://example.com/sale", customAlias = "never-created", expiresAt });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(message, (await ReadJsonAsync(response)).GetProperty("errors").GetProperty("expiresAt")[0].GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync(DetailsUri("never-created"))).StatusCode);
    }

    [Fact]
    public async Task RedirectsUntilExpiryThenReturns410WithoutCounting()
    {
        var code = await CreateLinkAsync(new { url = "https://example.com/sale", expiresAt = InMinutes(10) });
        var client = factory.CreateClient(NoRedirects);

        var before = await client.GetAsync(Relative(code));
        Assert.Equal(HttpStatusCode.Found, before.StatusCode);
        await Eventually.AssertAsync(async () => await ClickCountAsync(code) == 1, "the click before expiry counted");

        factory.Clock.Advance(TimeSpan.FromMinutes(11));
        var after = await client.GetAsync(Relative(code));

        Assert.Equal(HttpStatusCode.Gone, after.StatusCode);
        Assert.Equal("application/problem+json", after.Content.Headers.ContentType?.MediaType);
        Assert.True(after.Headers.CacheControl?.NoStore, "An expired response must not be cached: the alias can be reclaimed.");
        Assert.Null(after.Headers.Location);
        var problem = await ReadJsonAsync(after);
        Assert.Equal(410, problem.GetProperty("status").GetInt32());
        Assert.Equal("Link expired", problem.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.11", problem.GetProperty("type").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("example.com", problem.GetRawText(), StringComparison.Ordinal);
        await WaitForQueuedClicksAsync();
        Assert.Equal(1, await ClickCountAsync(code));
    }

    [Fact]
    public async Task ExpiredAliasReturns410InAnyCase()
    {
        await CreateLinkAsync(new { url = "https://example.com/sale", customAlias = "Flash-Sale", expiresAt = InMinutes(1) });

        factory.Clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(HttpStatusCode.Gone, (await factory.CreateClient(NoRedirects).GetAsync(Relative("FLASH-SALE"))).StatusCode);
    }

    [Fact]
    public async Task DetailsOfAnExpiredLinkAreStillReturned()
    {
        var expiresAt = factory.Clock.GetUtcNow().AddMinutes(5);
        var code = await CreateLinkAsync(new { url = "https://example.com/sale", expiresAt = expiresAt.ToString("O", CultureInfo.InvariantCulture) });

        factory.Clock.Advance(TimeSpan.FromDays(1));
        var response = await factory.CreateClient().GetAsync(DetailsUri(code));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(expiresAt.UtcDateTime, body.GetProperty("expiresAtUtc").GetDateTime().ToUniversalTime());
        Assert.EndsWith("Z", body.GetProperty("expiresAtUtc").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinksWithoutExpiryAreUnaffectedByTime()
    {
        var code = await CreateLinkAsync(new { url = "https://example.com/forever" });

        factory.Clock.Advance(TimeSpan.FromDays(3650));

        Assert.Equal(HttpStatusCode.Found, (await factory.CreateClient(NoRedirects).GetAsync(Relative(code))).StatusCode);
    }

    /// <summary>
    /// Clicks are stored in the background, in the order they were queued, by a single writer. Once a
    /// fresh marker click is stored, any click queued before it has been stored too, so an absent
    /// click really was never recorded.
    /// </summary>
    private async Task WaitForQueuedClicksAsync()
    {
        var marker = await CreateLinkAsync(new { url = "https://example.com/marker" });
        Assert.Equal(HttpStatusCode.Found, (await factory.CreateClient(NoRedirects).GetAsync(Relative(marker))).StatusCode);
        await Eventually.AssertAsync(async () => await ClickCountAsync(marker) == 1, "the marker click stored");
    }

    private string InMinutes(int minutes) =>
        factory.Clock.GetUtcNow().AddMinutes(minutes).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private async Task<string> CreateLinkAsync(object request)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("code").GetString()!;
    }

    private async Task<long> ClickCountAsync(string code) =>
        (await ReadJsonAsync(await factory.CreateClient().GetAsync(DetailsUri(code)))).GetProperty("clickCount").GetInt64();

    private static Uri Relative(string path) => new($"/{path}", UriKind.Relative);

    private static Uri DetailsUri(string code) => new($"/api/links/{code}", UriKind.Relative);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
}