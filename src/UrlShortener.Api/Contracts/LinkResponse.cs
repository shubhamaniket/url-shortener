using UrlShortener.Application.Links;

namespace UrlShortener.Api.Contracts;

/// <summary>A short link as returned by the API (contracts/openapi.yaml, <c>LinkResponse</c>).</summary>
public sealed record LinkResponse(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    DateTime CreatedAtUtc,
    long ClickCount)
{
    public static LinkResponse From(LinkDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return new(details.Code, details.ShortUrl, details.OriginalUrl, details.CreatedAtUtc, details.ClickCount);
    }
}