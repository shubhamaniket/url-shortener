namespace UrlShortener.Application.Options;

/// <summary>
/// Settings bound from the <c>ShortLinks</c> configuration section. The public base URL is the
/// only source for building short URLs and for the self-host check; the incoming request's host
/// is never used (FR-005a).
/// </summary>
public sealed class ShortLinkOptions
{
    public const string SectionName = "ShortLinks";

    /// <summary>Absolute http(s) root URL, e.g. <c>http://localhost:5058</c>.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>The validated base URL with a trailing slash. Call <see cref="Validate"/> first.</summary>
    public Uri BaseUri => new(PublicBaseUrl.Trim(), UriKind.Absolute);

    /// <summary>Returns an error message, or <c>null</c> when the settings are valid.</summary>
    public string? Validate()
    {
        var setting = $"{SectionName}:{nameof(PublicBaseUrl)}";

        if (string.IsNullOrWhiteSpace(PublicBaseUrl)
            || !Uri.TryCreate(PublicBaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return $"{setting} must be an absolute URL.";
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return $"{setting} must use http or https.";
        }

        if (uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return $"{setting} must be a root URL without user info, path, query, or fragment.";
        }

        return null;
    }

    /// <summary>Builds the public short URL for a code.</summary>
    public string BuildShortUrl(string code) => new Uri(BaseUri, code).ToString();
}