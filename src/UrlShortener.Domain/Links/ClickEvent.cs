namespace UrlShortener.Domain.Links;

/// <summary>One successful redirect of a short link (FR-007). It holds no visitor identity.</summary>
/// <remarks>
/// <see cref="Create"/> reduces the raw <c>Referer</c> and <c>User-Agent</c> values with
/// <see cref="Links.ReferrerHost"/> and <see cref="Links.UserAgentFamily"/>, so the raw values are
/// never stored (FR-008, FR-009). Events are immutable once created.
/// </remarks>
public sealed class ClickEvent
{
    // Used by EF Core when materializing from the database.
    private ClickEvent()
    {
        UserAgentFamily = Links.UserAgentFamily.Other;
    }

    private ClickEvent(long shortLinkId, DateTime occurredAtUtc, string? referrerHost, string userAgentFamily)
    {
        ShortLinkId = shortLinkId;
        OccurredAtUtc = occurredAtUtc;
        ReferrerHost = referrerHost;
        UserAgentFamily = userAgentFamily;
    }

    public long Id { get; private set; }

    public long ShortLinkId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>The referring site's host; <c>null</c> means no or an invalid referrer ("(direct)").</summary>
    public string? ReferrerHost { get; private set; }

    /// <summary>One of the fixed families in <see cref="Links.UserAgentFamily"/>.</summary>
    public string UserAgentFamily { get; private set; }

    /// <summary>Creates the event for one redirect, keeping only the referrer host and the browser family.</summary>
    /// <param name="shortLinkId">Id of the stored link that was followed.</param>
    /// <param name="occurredAtUtc">Time of the redirect, in UTC.</param>
    /// <param name="referer">Raw <c>Referer</c> header value, if any; only its host is kept.</param>
    /// <param name="userAgent">Raw <c>User-Agent</c> header value, if any; only its family is kept.</param>
    public static ClickEvent Create(long shortLinkId, DateTime occurredAtUtc, string? referer, string? userAgent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shortLinkId);
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Click time must be UTC.", nameof(occurredAtUtc));
        }

        return new ClickEvent(
            shortLinkId, occurredAtUtc, Links.ReferrerHost.From(referer), Links.UserAgentFamily.From(userAgent));
    }
}