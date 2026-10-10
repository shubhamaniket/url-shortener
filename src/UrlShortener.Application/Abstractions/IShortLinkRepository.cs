using UrlShortener.Domain.Links;

namespace UrlShortener.Application.Abstractions;

public interface IShortLinkRepository
{
    /// <summary>Finds the link whose normalized code equals <paramref name="normalizedCode"/>.</summary>
    Task<ShortLink?> FindByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the link. Returns <c>false</c> when its normalized code is already taken; the data
    /// store's unique constraint decides, so this is safe under concurrent requests.
    /// </summary>
    Task<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken);
}