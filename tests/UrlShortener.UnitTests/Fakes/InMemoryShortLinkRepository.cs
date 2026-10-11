using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Fakes;

/// <summary>
/// Mirrors the real repository's contract: one link per normalized code, and a new positive id for
/// every stored link, as the database assigns.
/// </summary>
internal sealed class InMemoryShortLinkRepository : IShortLinkRepository
{
    private readonly Dictionary<string, ShortLink> _links = new(StringComparer.Ordinal);
    private long _lastId;

    public IReadOnlyCollection<ShortLink> Links => _links.Values;

    public int AddAttempts { get; private set; }

    public int ReplaceAttempts { get; private set; }

    public Task<ShortLink?> FindByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<ShortLink?>(cancellationToken)
            : Task.FromResult(_links.GetValueOrDefault(normalizedCode));

    public Task<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken)
    {
        AddAttempts++;
        return Task.FromResult(_links.TryAdd(link.NormalizedCode, link) && AssignId(link));
    }

    public Task<bool> TryReplaceExpiredAsync(
        ShortLink expired, ShortLink replacement, DateTime nowUtc, CancellationToken cancellationToken)
    {
        ReplaceAttempts++;
        var replaced = _links.TryGetValue(expired.NormalizedCode, out var current)
            && ReferenceEquals(current, expired)
            && current.IsExpiredAt(nowUtc)
            && _links.Remove(expired.NormalizedCode)
            && _links.TryAdd(replacement.NormalizedCode, replacement)
            && AssignId(replacement);
        return Task.FromResult(replaced);
    }

    public void Seed(ShortLink link)
    {
        _links.Add(link.NormalizedCode, link);
        AssignId(link);
    }

    // The entity's id setter is private (EF Core sets it); the fake plays the database's part.
    private bool AssignId(ShortLink link)
    {
        typeof(ShortLink).GetProperty(nameof(ShortLink.Id))!.SetValue(link, ++_lastId);
        return true;
    }
}