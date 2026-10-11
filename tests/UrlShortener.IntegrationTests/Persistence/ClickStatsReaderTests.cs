using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Links;
using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests.Persistence;

/// <summary>The statistics queries against a real, migrated SQLite file with seeded clicks and a fixed "today".</summary>
public sealed class ClickStatsReaderTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 10, 11);
    private static readonly DateTime TodayStart = Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    private static readonly DateTime CreatedAt = TodayStart.AddDays(-100);

    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"urlshortener-stats-{Guid.NewGuid():N}.db");
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = $"Data Source={_databasePath}" })
            .Build();
        _services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<CapturingLoggerProvider>()
            .AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<CapturingLoggerProvider>())
            .AddLogging()
            .AddInfrastructure()
            .BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await Db(scope).Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    [Fact]
    public async Task LinkWithoutClicksHasThirtyZeroDaysEndingTodayAndNoReferrers()
    {
        var link = await AddLinkAsync("quiet");

        var stats = await GetAsync(link);

        Assert.Equal("quiet", stats.Code);
        Assert.Equal(0, stats.TotalClicks);
        Assert.Equal(LinkStatistics.Days, stats.Daily.Count);
        Assert.Equal(Today.AddDays(-29), stats.Daily[0].Date);
        Assert.Equal(Today, stats.Daily[^1].Date);
        Assert.Equal(Enumerable.Range(0, 30).Select(offset => Today.AddDays(-29 + offset)), stats.Daily.Select(day => day.Date));
        Assert.All(stats.Daily, day => Assert.Equal(0, day.Clicks));
        Assert.Empty(stats.TopReferrers);
    }

    [Fact]
    public async Task DailyWindowIsExactlyTheLastThirtyUtcDays()
    {
        var link = await AddLinkAsync("window");
        await AddClicksAsync(
            link,
            TodayStart.AddDays(-29),                    // first instant of the oldest day: included
            TodayStart.AddDays(-29).AddTicks(-1),       // last instant of day 30 back: excluded
            TodayStart.AddDays(-60),                    // long ago: excluded
            TodayStart.AddHours(9),                     // today
            TodayStart.AddDays(1).AddTicks(-1),         // last instant of today
            TodayStart.AddDays(-1).AddHours(23));       // yesterday

        var stats = await GetAsync(link);

        Assert.Equal(1, Clicks(stats, Today.AddDays(-29)));
        Assert.Equal(1, Clicks(stats, Today.AddDays(-1)));
        Assert.Equal(2, Clicks(stats, Today));
        Assert.Equal(4, stats.Daily.Sum(day => day.Clicks));
    }

    [Fact]
    public async Task TotalIsTheStoredCountIncludingClicksFromBeforeHistoryWasKept()
    {
        var link = await AddLinkAsync("legacy");
        await AddClicksAsync(link, TodayStart.AddHours(1), TodayStart.AddHours(2));
        await using (var scope = _services.CreateAsyncScope())
        {
            // 7 clicks counted by feature 001, before per-click events existed, plus the 2 above.
            await Db(scope).ShortLinks.Where(stored => stored.Id == link.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.ClickCount, 9));
        }

        var stats = await GetAsync(link);

        Assert.Equal(9, stats.TotalClicks);
        Assert.Equal(2, stats.Daily.Sum(day => day.Clicks));
    }

    [Fact]
    public async Task TopFiveReferrersByAllTimeClicksWithTiesAlphabetical()
    {
        var link = await AddLinkAsync("sources");
        await AddClicksFromAsync(link, ("https://b.example/x?u=1", 3), ("https://a.example/", 3), (null, 2),
            ("https://c.example/", 2), ("https://f.example/", 1), ("https://e.example/", 1), ("https://d.example/", 1));

        var stats = await GetAsync(link);

        Assert.Equal(
            [new("a.example", 3), new("b.example", 3), new(LinkStatistics.DirectSource, 2), new("c.example", 2), new ReferrerClicks("d.example", 1)],
            stats.TopReferrers);
    }

    [Fact]
    public async Task DirectRanksByItsCountLikeAnyOtherSource()
    {
        var link = await AddLinkAsync("direct");
        await AddClicksFromAsync(link, (null, 4), ("not a url", 1), ("https://news.example/a", 2));

        var stats = await GetAsync(link);

        Assert.Equal([new(LinkStatistics.DirectSource, 5), new ReferrerClicks("news.example", 2)], stats.TopReferrers);
    }

    [Fact]
    public async Task ReferrersCountAllTimeAndOnlyThisLink()
    {
        var link = await AddLinkAsync("mine");
        var other = await AddLinkAsync("other");
        await AddClicksAsync(link, "https://old.example/", TodayStart.AddDays(-90));
        await AddClicksAsync(other, "https://other.example/", TodayStart);

        var stats = await GetAsync(link);

        Assert.Equal([new ReferrerClicks("old.example", 1)], stats.TopReferrers);
        Assert.All(stats.Daily, day => Assert.Equal(0, day.Clicks));
    }

    [Fact]
    public async Task BothAggregationsRunInTheDatabase()
    {
        var link = await AddLinkAsync("sql");
        await AddClicksAsync(link, TodayStart);
        var logs = _services.GetRequiredService<CapturingLoggerProvider>();
        var before = logs.Entries.Count;

        await GetAsync(link);

        var commands = logs.Entries.Skip(before)
            .Where(entry => entry.Category == "Microsoft.EntityFrameworkCore.Database.Command" && entry.Message.Contains("ClickEvents", StringComparison.Ordinal))
            .Select(entry => entry.Message)
            .ToList();
        Assert.Equal(2, commands.Count);
        Assert.All(commands, sql => Assert.Contains("GROUP BY", sql, StringComparison.Ordinal));
        Assert.Contains(commands, sql => sql.Contains("LIMIT", StringComparison.Ordinal));
    }

    private static long Clicks(LinkStatistics stats, DateOnly date) => stats.Daily.Single(day => day.Date == date).Clicks;

    private async Task<LinkStatistics> GetAsync(ShortLink link)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IClickStatsReader>().GetAsync(link, Today, CancellationToken.None);
    }

    private async Task<ShortLink> AddLinkAsync(string alias)
    {
        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IShortLinkRepository>();
        Assert.True(await repository.TryAddAsync(
            ShortLink.CreateWithCustomAlias(alias, "https://example.com/a", CreatedAt), CancellationToken.None));
        return (await repository.FindByNormalizedCodeAsync(alias, CancellationToken.None))!;
    }

    private Task AddClicksAsync(ShortLink link, params DateTime[] times) => AddClicksAsync(link, null, times);

    private async Task AddClicksAsync(ShortLink link, string? referer, params DateTime[] times)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);
        db.ClickEvents.AddRange(times.Select(time => ClickEvent.Create(link.Id, time, referer, null)));
        await db.SaveChangesAsync();
    }

    private async Task AddClicksFromAsync(ShortLink link, params (string? Referer, int Count)[] sources)
    {
        foreach (var (referer, count) in sources)
        {
            await AddClicksAsync(link, referer, Enumerable.Repeat(TodayStart.AddDays(-45), count).ToArray());
        }
    }

    private static AppDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<AppDbContext>();
}