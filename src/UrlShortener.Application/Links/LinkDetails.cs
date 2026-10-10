namespace UrlShortener.Application.Links;

/// <summary>A link as returned to callers; <see cref="ShortUrl"/> is built from the configured base URL.</summary>
public sealed record LinkDetails(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    DateTime CreatedAtUtc,
    long ClickCount);