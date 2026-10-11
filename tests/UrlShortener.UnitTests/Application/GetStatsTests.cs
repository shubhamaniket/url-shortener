using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;

using UrlShortener.Application.Links;
using UrlShortener.Application.Options;
using UrlShortener.Domain.Links;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Application;

public class GetStatsTests
{
    private static readonly DateTime CreatedAt = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryShortLinkRepository _repository = new();
    private readonly FakeClickRecorder _recorder = new();
    private readonly FakeClickStatsReader _reader = new();

    public GetStatsTests()
    {
        _repository.Seed(ShortLink.CreateWithGeneratedCode("Abc123x", "https://example.com/a", CreatedAt));
        _repository.Seed(ShortLink.CreateWithCustomAlias("Team-Offsite", "https://example.com/offsite", CreatedAt, CreatedAt.AddDays(1)));
    }

    // A server whose local zone is far from UTC: its local date differs from the UTC date.
    private static readonly TimeZoneInfo FarEast = TimeZoneInfo.CreateCustomTimeZone("Test+14", TimeSpan.FromHours(14), "Test+14", "Test+14");

    [Theory]
    [InlineData("2026-10-10T23:30:00-05:00", "2026-10-11")] // already tomorrow in UTC
    [InlineData("2026-10-10T20:00:00Z", "2026-10-10")]      // already tomorrow at UTC+14 local time
    public async Task ReadsStatisticsForTodayInUtcWithoutRecordingAClick(string now, string expectedTodayUtc)
    {
        var stats = await CreateService(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture), FarEast)
            .GetStatsAsync("Abc123x", CancellationToken.None);

        Assert.NotNull(stats);
        Assert.Equal("Abc123x", stats.Code);
        var (link, today) = Assert.Single(_reader.Requests);
        Assert.Equal("Abc123x", link.Code);
        Assert.Equal(DateOnly.Parse(expectedTodayUtc, CultureInfo.InvariantCulture), today);
        Assert.Empty(_recorder.Recorded);
    }

    [Theory]
    [InlineData("team-offsite")]
    [InlineData("TEAM-OFFSITE")]
    public async Task AliasMatchesInAnyCaseAndKeepsItsOriginalCase(string code)
    {
        var stats = await CreateService(new DateTimeOffset(CreatedAt)).GetStatsAsync(code, CancellationToken.None);

        Assert.Equal("Team-Offsite", stats?.Code);
    }

    [Fact]
    public async Task ExpiredLinkStillHasStatistics()
    {
        var stats = await CreateService(new DateTimeOffset(CreatedAt.AddDays(30))).GetStatsAsync("team-offsite", CancellationToken.None);

        Assert.NotNull(stats);
        Assert.Single(_reader.Requests);
    }

    [Theory]
    [InlineData("zzzzzzz")]
    [InlineData("abc123x")]
    public async Task UnknownCodeOrGeneratedCodeInWrongCaseHasNoStatistics(string code)
    {
        var stats = await CreateService(new DateTimeOffset(CreatedAt)).GetStatsAsync(code, CancellationToken.None);

        Assert.Null(stats);
        Assert.Empty(_reader.Requests);
    }

    private LinkService CreateService(DateTimeOffset now, TimeZoneInfo? localTimeZone = null) => new(
        _repository,
        new ScriptedCodeGenerator("Unused0"),
        _recorder,
        _reader,
        Microsoft.Extensions.Options.Options.Create(new ShortLinkOptions { PublicBaseUrl = "http://localhost:5058" }),
        new FixedTimeProvider(now, localTimeZone),
        NullLogger<LinkService>.Instance);
}