using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Fakes;

/// <summary>Mirrors the real repository's contract: one link per normalized code.</summary>
internal sealed class InMemoryShortLinkRepository : IShortLinkRepository
{
    private readonly Dictionary<string, ShortLink> _links = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ShortLink> Links => _links.Values;

    public int AddAttempts { get; private set; }

    public int ReplaceAttempts { get; private set; }

    public Task<ShortLink?> FindByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken) =>
        Task.FromResult(_links.GetValueOrDefault(normalizedCode));

    public Task<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken)
    {
        AddAttempts++;
        return Task.FromResult(_links.TryAdd(link.NormalizedCode, link));
    }

    public Task<bool> TryReplaceExpiredAsync(
        ShortLink expired, ShortLink replacement, DateTime nowUtc, CancellationToken cancellationToken)
    {
        ReplaceAttempts++;
        var replaced = _links.TryGetValue(expired.NormalizedCode, out var current)
            && ReferenceEquals(current, expired)
            && current.IsExpiredAt(nowUtc)
            && _links.Remove(expired.NormalizedCode)
            && _links.TryAdd(replacement.NormalizedCode, replacement);
        return Task.FromResult(replaced);
    }

    public void Seed(ShortLink link) => _links.Add(link.NormalizedCode, link);
}