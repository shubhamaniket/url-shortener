using UrlShortener.Application.Links;

namespace UrlShortener.Api.Contracts;

/// <summary>
/// A link's click statistics as returned by the API (contracts/openapi.yaml, <c>LinkStatsResponse</c>).
/// Aggregates only: no per-click or per-visitor data (FR-020).
/// </summary>
public sealed record LinkStatsResponse(
    string Code,
    long TotalClicks,
    IReadOnlyList<DailyClicksResponse> DailyClicks,
    IReadOnlyList<ReferrerClicksResponse> TopReferrers)
{
    public static LinkStatsResponse From(LinkStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        return new(
            statistics.Code,
            statistics.TotalClicks,
            statistics.Daily.Select(day => new DailyClicksResponse(day.Date, day.Clicks)).ToList(),
            statistics.TopReferrers.Select(source => new ReferrerClicksResponse(source.Source, source.Clicks)).ToList());
    }
}

/// <summary>Clicks on one UTC day; <see cref="Date"/> is serialized as <c>yyyy-MM-dd</c>.</summary>
public sealed record DailyClicksResponse(DateOnly Date, long Clicks);

/// <summary>Clicks from one referring host, or <c>(direct)</c> for clicks without a referrer.</summary>
public sealed record ReferrerClicksResponse(string Source, long Clicks);