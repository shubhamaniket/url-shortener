using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Reclaiming an expired alias end to end (feature 002, FR-006a): the old link and its click history
/// are deleted and the code serves the new link. Expiries are relative to the forward-only clock.
/// </summary>
public sealed class ReclaimTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri LinksUri = new("/api/links", UriKind.Relative);
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task ExpiredAliasIsReclaimedInAnotherCaseAndServesTheNewLink()
    {
        var alias = UniqueAlias("promo");
        var oldCode = await CreateLinkAsync(new { url = "https://example.com/old", customAlias = alias, expiresAt = InMinutes(1) });
        var client = factory.CreateClient(NoRedirects);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync(Relative(oldCode))).StatusCode);
        var oldId = await LinkIdAsync(oldCode);
        await AddClickEventsAsync(oldId, 3);

        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        var response = await factory.CreateClient().PostAsJsonAsync(
            LinksUri, new { url = "https://example.com/new", customAlias = alias.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadJsonAsync(response);
        Assert.Equal(alias.ToUpperInvariant(), created.GetProperty("code").GetString());
        Assert.Equal(0, created.GetProperty("clickCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("expiresAtUtc").ValueKind);

        var redirect = await client.GetAsync(Relative(alias));
        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal("https://example.com/new", redirect.Headers.Location?.OriginalString);

        var details = await ReadJsonAsync(await factory.CreateClient().GetAsync(DetailsUri(alias)));
        Assert.Equal("https://example.com/new", details.GetProperty("originalUrl").GetString());
        Assert.Equal(1, details.GetProperty("clickCount").GetInt64());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.ShortLinks.AnyAsync(link => link.Id == oldId));
        Assert.Equal(0, await db.ClickEvents.CountAsync(click => click.ShortLinkId == oldId));
    }

    [Fact]
    public async Task AliasOfALinkNotYetExpiredIsStillAConflict()
    {
        var alias = UniqueAlias("live");
        await CreateLinkAsync(new { url = "https://example.com/old", customAlias = alias, expiresAt = InMinutes(60) });

        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, new { url = "https://example.com/new", customAlias = alias });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var redirect = await factory.CreateClient(NoRedirects).GetAsync(Relative(alias));
        Assert.Equal("https://example.com/old", redirect.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ReclaimedLinkCanItselfExpire()
    {
        var alias = UniqueAlias("again");
        await CreateLinkAsync(new { url = "https://example.com/first", customAlias = alias, expiresAt = InMinutes(1) });
        factory.Clock.Advance(TimeSpan.FromMinutes(2));

        await CreateLinkAsync(new { url = "https://example.com/second", customAlias = alias, expiresAt = InMinutes(1) });
        factory.Clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(HttpStatusCode.Gone, (await factory.CreateClient(NoRedirects).GetAsync(Relative(alias))).StatusCode);
    }

    private static string UniqueAlias(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private string InMinutes(int minutes) =>
        factory.Clock.GetUtcNow().AddMinutes(minutes).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private async Task<long> LinkIdAsync(string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var normalized = ShortLink.Normalize(code);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks
            .Where(link => link.NormalizedCode == normalized).Select(link => link.Id).SingleAsync();
    }

    // Click events are written by the background writer from feature 002 task T012; inserted directly here.
    private async Task AddClickEventsAsync(long linkId, int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        db.ClickEvents.AddRange(Enumerable.Range(0, count).Select(_ => ClickEvent.Create(linkId, now, "https://news.example/a", null)));
        await db.SaveChangesAsync();
    }

    private async Task<string> CreateLinkAsync(object request)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("code").GetString()!;
    }

    private static Uri Relative(string path) => new($"/{path}", UriKind.Relative);

    private static Uri DetailsUri(string code) => new($"/api/links/{code}", UriKind.Relative);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
}