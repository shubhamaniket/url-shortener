using Microsoft.EntityFrameworkCore;

using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Links;
using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Links;

/// <summary>
/// Statistics queries (research R7). Both aggregations run in the database and use the
/// (ShortLinkId, OccurredAtUtc) index; only the padding to 30 days happens in memory.
/// </summary>
public sealed class ClickStatsReader(AppDbContext db) : IClickStatsReader
{
    public async Task<LinkStatistics> GetAsync(ShortLink link, DateOnly todayUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);

        var total = await db.ShortLinks
            .Where(stored => stored.Id == link.Id)
            .Select(stored => stored.ClickCount)
            .SingleOrDefaultAsync(cancellationToken);

        var firstDay = todayUtc.AddDays(-(LinkStatistics.Days - 1));
        var windowStart = firstDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var windowEnd = todayUtc.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var perDay = await db.ClickEvents
            .Where(click => click.ShortLinkId == link.Id && click.OccurredAtUtc >= windowStart && click.OccurredAtUtc < windowEnd)
            .GroupBy(click => click.OccurredAtUtc.Date)
            .Select(day => new { Day = day.Key, Clicks = day.LongCount() })
            .ToDictionaryAsync(day => DateOnly.FromDateTime(day.Day), day => day.Clicks, cancellationToken);

        var daily = Enumerable.Range(0, LinkStatistics.Days)
            .Select(offset => firstDay.AddDays(offset))
            .Select(date => new DailyClicks(date, perDay.GetValueOrDefault(date)))
            .ToList();

        var topReferrers = await db.ClickEvents
            .Where(click => click.ShortLinkId == link.Id)
            .GroupBy(click => click.ReferrerHost ?? LinkStatistics.DirectSource)
            .Select(source => new { Source = source.Key, Clicks = source.LongCount() })
            .OrderByDescending(source => source.Clicks)
            .ThenBy(source => source.Source)
            .Take(LinkStatistics.TopReferrerCount)
            .ToListAsync(cancellationToken);

        return new LinkStatistics(
            link.Code,
            total,
            daily,
            topReferrers.Select(source => new ReferrerClicks(source.Source, source.Clicks)).ToList());
    }
}