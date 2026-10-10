namespace UrlShortener.Domain.Links;

/// <summary>
/// Reduces a raw <c>User-Agent</c> header to a coarse browser family (FR-009), so the full string,
/// which can help identify a device, is never stored or logged.
/// </summary>
public static class UserAgentFamily
{
    public const int MaxLength = 16;

    public const string Bot = "Bot";
    public const string Edge = "Edge";
    public const string Firefox = "Firefox";
    public const string Chrome = "Chrome";
    public const string Safari = "Safari";
    public const string Other = "Other";

    // First match wins. The order matters: crawlers often embed browser tokens, Edge's user agent
    // also contains "Chrome/", and Chrome's also contains "Safari/".
    private static readonly (string Family, string[] Markers)[] Rules =
    [
        (Bot, ["bot", "crawler", "spider", "slurp", "facebookexternalhit", "whatsapp", "preview"]),
        (Edge, ["Edg/"]),
        (Firefox, ["Firefox/"]),
        (Chrome, ["Chrome/", "CriOS/"]),
        (Safari, ["Safari/"]),
    ];

    /// <summary>Returns one of the fixed families; missing or unrecognised user agents are <see cref="Other"/>.</summary>
    public static string From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return Other;
        }

        foreach (var (family, markers) in Rules)
        {
            if (markers.Any(marker => userAgent.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return family;
            }
        }

        return Other;
    }
}