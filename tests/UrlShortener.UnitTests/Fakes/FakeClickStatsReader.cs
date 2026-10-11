using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Links;
using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Fakes;

/// <summary>Records each request and returns the stored total with no per-day or referrer data.</summary>
internal sealed class FakeClickStatsReader : IClickStatsReader
{
    public List<(ShortLink Link, DateOnly TodayUtc)> Requests { get; } = [];

    public Task<LinkStatistics> GetAsync(ShortLink link, DateOnly todayUtc, CancellationToken cancellationToken)
    {
        Requests.Add((link, todayUtc));
        return Task.FromResult(new LinkStatistics(link.Code, link.ClickCount, [], []));
    }
}