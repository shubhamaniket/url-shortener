using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure.Links;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Redirect → queue → background writer, end to end (feature 002, US2): what is stored, and that a
/// storage failure never reaches the redirect.
/// </summary>
public sealed class ClickRecordingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RawReferer = "https://user:secret@News.Example/article?u=42#comments";
    private const string RawUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36 Edg/129.0.0.0";

    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task StoredClickHoldsOnlyTheReferrerHostBrowserFamilyAndTime()
    {
        var code = await CreateLinkAsync("https://example.com/private");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/{code}", UriKind.Relative));
        request.Headers.Referrer = new Uri(RawReferer);
        request.Headers.UserAgent.ParseAdd(RawUserAgent);

        var before = factory.Clock.GetUtcNow().UtcDateTime;
        Assert.Equal(HttpStatusCode.Found, (await factory.CreateClient(NoRedirects).SendAsync(request)).StatusCode);

        var linkId = await LinkIdAsync(code);
        await Eventually.AssertAsync(async () => await EventCountAsync(linkId) == 1, "the click stored");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var click = await db.ClickEvents.AsNoTracking().SingleAsync(c => c.ShortLinkId == linkId);
        Assert.Equal("news.example", click.ReferrerHost);
        Assert.Equal(UserAgentFamily.Edge, click.UserAgentFamily);
        Assert.InRange(click.OccurredAtUtc, before, factory.Clock.GetUtcNow().UtcDateTime);

        // The table has no other columns (no IP, no full referrer, no raw user agent), and no stored
        // value contains any part of the raw headers.
        var row = await RawRowAsync(db, linkId);
        Assert.Equal(["Id", "OccurredAtUtc", "ReferrerHost", "ShortLinkId", "UserAgentFamily"], row.Keys.Order(StringComparer.Ordinal));
        foreach (var fragment in new[] { "/article", "u=42", "secret", "comments", "Mozilla", "Windows" })
        {
            Assert.DoesNotContain(row.Values, value => value?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    [Fact]
    public async Task RedirectSucceedsWhenClickStorageFailsAndTheFailureIsLogged()
    {
        var code = await CreateLinkAsync("https://example.com/resilient-storage");
        using var logs = new CapturingLoggerProvider();
        await using var failingStorage = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureTestServices(services => services.AddScoped<IClickEventStore, ThrowingStore>());
        });

        var response = await failingStorage.CreateClient(NoRedirects).GetAsync(new Uri($"/{code}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://example.com/resilient-storage", response.Headers.Location?.OriginalString);
        await Eventually.AssertAsync(
            () => Task.FromResult(logs.Entries.Any(entry =>
                entry.Level == LogLevel.Error && entry.Message == "Storing a batch of 1 clicks failed; the batch was discarded")),
            "the storage failure logged");
    }

    private async Task<string> CreateLinkAsync(string url)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(new Uri("/api/links", UriKind.Relative), new { url });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
    }

    private async Task<long> LinkIdAsync(string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks
            .Where(link => link.Code == code).Select(link => link.Id).SingleAsync();
    }

    private async Task<int> EventCountAsync(long linkId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ClickEvents.CountAsync(click => click.ShortLinkId == linkId);
    }

    private static async Task<Dictionary<string, string?>> RawRowAsync(AppDbContext db, long linkId)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM ClickEvents WHERE ShortLinkId = {linkId}";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return Enumerable.Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i).ToString());
    }

    private sealed class ThrowingStore : IClickEventStore
    {
        public Task<int> SaveAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("database is locked");
    }
}