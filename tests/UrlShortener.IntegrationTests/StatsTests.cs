using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace UrlShortener.IntegrationTests;

/// <summary><c>GET /api/links/{code}/stats</c> end to end (feature 002, US3): real redirects, real background storage.</summary>
public sealed class StatsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri LinksUri = new("/api/links", UriKind.Relative);
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task CountsClicksPerDayAndPerReferrer()
    {
        var code = await CreateLinkAsync(new { url = "https://example.com/stats" });
        for (var i = 0; i < 3; i++)
        {
            await FollowAsync(code, referer: "https://news.example/a?u=1");
        }

        for (var i = 0; i < 2; i++)
        {
            await FollowAsync(code, referer: null);
        }

        await Eventually.AssertAsync(async () => (await StatsAsync(code)).GetProperty("totalClicks").GetInt64() == 5, "five clicks stored");
        var response = await factory.CreateClient().GetAsync(StatsUri(code));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var stats = await ReadJsonAsync(response);
        Assert.Equal(["code", "dailyClicks", "topReferrers", "totalClicks"], stats.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(code, stats.GetProperty("code").GetString());
        var daily = stats.GetProperty("dailyClicks").EnumerateArray().ToList();
        Assert.Equal(30, daily.Count);
        Assert.Equal(Today(), daily[^1].GetProperty("date").GetString());
        Assert.Equal(5, daily[^1].GetProperty("clicks").GetInt64());
        Assert.All(daily[..^1], day => Assert.Equal(0, day.GetProperty("clicks").GetInt64()));
        Assert.Equal(
            [("news.example", 3L), ("(direct)", 2L)],
            stats.GetProperty("topReferrers").EnumerateArray().Select(r => (r.GetProperty("source").GetString(), r.GetProperty("clicks").GetInt64())));
    }

    [Fact]
    public async Task ReadingStatisticsIsNotAClick()
    {
        var code = await CreateLinkAsync(new { url = "https://example.com/read-only" });
        await FollowAsync(code, referer: null);
        await Eventually.AssertAsync(async () => (await StatsAsync(code)).GetProperty("totalClicks").GetInt64() == 1, "the click stored");

        await StatsAsync(code);
        await StatsAsync(code);
        await WaitForQueuedClicksAsync();

        Assert.Equal(1, (await StatsAsync(code)).GetProperty("totalClicks").GetInt64());
    }

    [Fact]
    public async Task AliasStatisticsInAnyCaseUseTheOriginalCase()
    {
        var alias = $"Stats-{Guid.NewGuid():N}"[..20];
        await CreateLinkAsync(new { url = "https://example.com/alias", customAlias = alias });

        var stats = await StatsAsync(alias.ToUpperInvariant());

        Assert.Equal(alias, stats.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExpiredLinkStillHasStatisticsAndAReclaimedCodeStartsFromZero()
    {
        var alias = $"season-{Guid.NewGuid():N}"[..20];
        await CreateLinkAsync(new { url = "https://example.com/old", customAlias = alias, expiresAt = InMinutes(1) });
        await FollowAsync(alias, referer: "https://old.example/");
        await Eventually.AssertAsync(async () => (await StatsAsync(alias)).GetProperty("totalClicks").GetInt64() == 1, "the old link's click stored");
        factory.Clock.Advance(TimeSpan.FromMinutes(2));

        var expired = await factory.CreateClient().GetAsync(StatsUri(alias));
        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(expired)).GetProperty("totalClicks").GetInt64());

        await CreateLinkAsync(new { url = "https://example.com/new", customAlias = alias });
        var reclaimed = await StatsAsync(alias);

        Assert.Equal(0, reclaimed.GetProperty("totalClicks").GetInt64());
        Assert.All(reclaimed.GetProperty("dailyClicks").EnumerateArray(), day => Assert.Equal(0, day.GetProperty("clicks").GetInt64()));
        Assert.Empty(reclaimed.GetProperty("topReferrers").EnumerateArray());
    }

    [Theory]
    [InlineData("zzzzzzz")]
    [InlineData("not-a-real-alias")]
    public async Task UnknownCodeReturnsProblemDetails404(string code)
    {
        var response = await factory.CreateClient().GetAsync(StatsUri(code));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Clicks are stored in queue order by one writer; once a fresh marker click is stored, earlier ones are too.</summary>
    private async Task WaitForQueuedClicksAsync()
    {
        var marker = await CreateLinkAsync(new { url = "https://example.com/marker" });
        await FollowAsync(marker, referer: null);
        await Eventually.AssertAsync(async () => (await StatsAsync(marker)).GetProperty("totalClicks").GetInt64() == 1, "the marker click stored");
    }

    private string Today() => factory.Clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private string InMinutes(int minutes) =>
        factory.Clock.GetUtcNow().AddMinutes(minutes).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private async Task FollowAsync(string code, string? referer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/{code}", UriKind.Relative));
        if (referer is not null)
        {
            request.Headers.Referrer = new Uri(referer);
        }

        Assert.Equal(HttpStatusCode.Found, (await factory.CreateClient(NoRedirects).SendAsync(request)).StatusCode);
    }

    private async Task<JsonElement> StatsAsync(string code)
    {
        var response = await factory.CreateClient().GetAsync(StatsUri(code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private async Task<string> CreateLinkAsync(object request)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("code").GetString()!;
    }

    private static Uri StatsUri(string code) => new($"/api/links/{code}/stats", UriKind.Relative);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
}