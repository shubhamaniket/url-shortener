namespace UrlShortener.Application.Links;

/// <summary>
/// Aggregated clicks for one link (FR-016–FR-020): no per-click or per-visitor data.
/// </summary>
/// <param name="Code">The link's code as created.</param>
/// <param name="TotalClicks">The stored all-time total, including clicks from before per-click history existed.</param>
/// <param name="Daily">Exactly <see cref="Days"/> UTC days, oldest first, ending today; days without clicks are 0.</param>
/// <param name="TopReferrers">Up to <see cref="TopReferrerCount"/> sources by all-time clicks, highest first, ties alphabetical.</param>
public sealed record LinkStatistics(
    string Code,
    long TotalClicks,
    IReadOnlyList<DailyClicks> Daily,
    IReadOnlyList<ReferrerClicks> TopReferrers)
{
    public const int Days = 30;
    public const int TopReferrerCount = 5;

    /// <summary>The source shown for clicks without a (valid) referrer.</summary>
    public const string DirectSource = "(direct)";
}

public sealed record DailyClicks(DateOnly Date, long Clicks);

public sealed record ReferrerClicks(string Source, long Clicks);