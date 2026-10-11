using System.Diagnostics.CodeAnalysis;

namespace UrlShortener.Application.Links;

/// <summary>The outcome of following a short code (research R2): a destination, unknown, or expired.</summary>
public sealed record RedirectResolution
{
    private RedirectResolution(RedirectOutcome outcome, string? url)
    {
        Outcome = outcome;
        Url = url;
    }

    public static RedirectResolution NotFound { get; } = new(RedirectOutcome.NotFound, null);

    /// <summary>The link exists but has expired; no click is recorded (FR-004).</summary>
    public static RedirectResolution Expired { get; } = new(RedirectOutcome.Expired, null);

    public RedirectOutcome Outcome { get; }

    /// <summary>The original URL to redirect to; set only when <see cref="IsFound"/>.</summary>
    public string? Url { get; }

    [MemberNotNullWhen(true, nameof(Url))]
    public bool IsFound => Outcome == RedirectOutcome.Found;

    public static RedirectResolution Found(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return new(RedirectOutcome.Found, url);
    }
}

public enum RedirectOutcome
{
    NotFound,
    Found,
    Expired,
}