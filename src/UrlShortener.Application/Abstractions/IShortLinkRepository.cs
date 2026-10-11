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

    /// <summary>
    /// Atomically retires <paramref name="expired"/> (with its click history) and inserts
    /// <paramref name="replacement"/> under the same code. Returns <c>false</c>, changing nothing, when
    /// the link is no longer there or no longer expired at <paramref name="nowUtc"/>, or the code was
    /// taken meanwhile; so of several concurrent claims at most one succeeds (research R6).
    /// </summary>
    Task<bool> TryReplaceExpiredAsync(
        ShortLink expired, ShortLink replacement, DateTime nowUtc, CancellationToken cancellationToken);
}