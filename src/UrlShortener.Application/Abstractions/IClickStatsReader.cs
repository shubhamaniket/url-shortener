using UrlShortener.Application.Links;
using UrlShortener.Domain.Links;

namespace UrlShortener.Application.Abstractions;

public interface IClickStatsReader
{
    /// <summary>
    /// Aggregates <paramref name="link"/>'s clicks: the stored total, the <see cref="LinkStatistics.Days"/>
    /// UTC days ending on <paramref name="todayUtc"/>, and the all-time top referrers (research R7).
    /// </summary>
    Task<LinkStatistics> GetAsync(ShortLink link, DateOnly todayUtc, CancellationToken cancellationToken);
}