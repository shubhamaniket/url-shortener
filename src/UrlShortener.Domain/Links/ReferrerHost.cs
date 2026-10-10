namespace UrlShortener.Domain.Links;

/// <summary>
/// Reduces a raw <c>Referer</c> header to the referring site's host (FR-008), so the path, query,
/// and any credentials it carried are never stored or logged.
/// </summary>
public static class ReferrerHost
{
    /// <summary>The longest DNS host name.</summary>
    public const int MaxLength = 253;

    /// <summary>
    /// Returns the lower-cased host of an absolute http or https referrer, or <c>null</c> when the
    /// referrer is missing, invalid, uses another scheme, or has an over-long host (shown as
    /// "(direct)" in statistics).
    /// </summary>
    public static string? From(string? referer)
    {
        var trimmed = referer?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        // IdnHost is ASCII (punycode), so the stored value is plain text whatever the site's script.
        var host = uri.IdnHost.ToLowerInvariant();
        return host.Length is > 0 and <= MaxLength ? host : null;
    }
}