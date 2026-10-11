using Microsoft.Extensions.Logging.Abstractions;

using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Links;
using UrlShortener.Application.Options;
using UrlShortener.Domain.Links;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Application;

public class ReclaimTests
{
    private static readonly DateTime CreatedAt = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresAt = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryShortLinkRepository _repository = new();

    [Fact]
    public async Task AliasHeldByAnExpiredLinkIsReclaimed()
    {
        _repository.Seed(ShortLink.CreateWithCustomAlias("promo", "https://example.com/old", CreatedAt, ExpiresAt));

        var details = await CreateService(ExpiresAt, "Unused0").CreateAsync(
            new CreateLinkCommand("https://example.com/new", "PROMO"), CancellationToken.None);

        Assert.Equal("PROMO", details.Code);
        Assert.Equal("https://example.com/new", details.OriginalUrl);
        Assert.Null(details.ExpiresAtUtc);
        Assert.Equal(0, details.ClickCount);
        var stored = Assert.Single(_repository.Links);
        Assert.Equal("https://example.com/new", stored.OriginalUrl);
        Assert.Equal(1, _repository.ReplaceAttempts);
    }

    [Fact]
    public async Task AliasHeldByAnExpiredGeneratedCodeIsReclaimed()
    {
        _repository.Seed(ShortLink.CreateWithGeneratedCode("Abc123x", "https://example.com/old", CreatedAt, ExpiresAt));

        var details = await CreateService(ExpiresAt.AddDays(1), "Unused0").CreateAsync(
            new CreateLinkCommand("https://example.com/new", "abc123x"), CancellationToken.None);

        Assert.Equal("abc123x", details.Code);
        Assert.True(Assert.Single(_repository.Links).IsCustomAlias);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-86_400)]
    public async Task AliasHeldByALinkNotYetExpiredIsAConflict(int secondsFromExpiry)
    {
        _repository.Seed(ShortLink.CreateWithCustomAlias("promo", "https://example.com/old", CreatedAt, ExpiresAt));

        await Assert.ThrowsAsync<AliasAlreadyExistsException>(() => CreateService(ExpiresAt.AddSeconds(secondsFromExpiry), "Unused0")
            .CreateAsync(new CreateLinkCommand("https://example.com/new", "promo"), CancellationToken.None));

        Assert.Equal("https://example.com/old", Assert.Single(_repository.Links).OriginalUrl);
        Assert.Equal(0, _repository.ReplaceAttempts);
    }

    [Fact]
    public async Task AliasHeldByALinkThatNeverExpiresIsAConflict()
    {
        _repository.Seed(ShortLink.CreateWithCustomAlias("promo", "https://example.com/old", CreatedAt));

        await Assert.ThrowsAsync<AliasAlreadyExistsException>(() => CreateService(ExpiresAt.AddYears(10), "Unused0")
            .CreateAsync(new CreateLinkCommand("https://example.com/new", "promo"), CancellationToken.None));

        Assert.Equal(0, _repository.ReplaceAttempts);
    }

    [Fact]
    public async Task LosingAConcurrentReclaimIsAConflict()
    {
        // Another request replaced the expired link between this request's lookup and its replace.
        var expired = ShortLink.CreateWithCustomAlias("promo", "https://example.com/old", CreatedAt, ExpiresAt);
        _repository.Seed(expired);
        var winner = ShortLink.CreateWithCustomAlias("Promo", "https://example.com/winner", ExpiresAt);
        Assert.True(await _repository.TryReplaceExpiredAsync(expired, winner, ExpiresAt, CancellationToken.None));
        var staleRepository = new StaleLookupRepository(_repository, expired);

        await Assert.ThrowsAsync<AliasAlreadyExistsException>(() => CreateService(ExpiresAt, staleRepository)
            .CreateAsync(new CreateLinkCommand("https://example.com/loser", "promo"), CancellationToken.None));

        Assert.Equal("https://example.com/winner", Assert.Single(_repository.Links).OriginalUrl);
    }

    [Fact]
    public async Task GeneratedCodeCollidingWithAnExpiredLinkRetriesInsteadOfReclaiming()
    {
        _repository.Seed(ShortLink.CreateWithGeneratedCode("Abc123x", "https://example.com/old", CreatedAt, ExpiresAt));

        var details = await CreateService(ExpiresAt.AddDays(1), "Abc123x", "Xyz789q").CreateAsync(
            new CreateLinkCommand("https://example.com/new", null), CancellationToken.None);

        Assert.Equal("Xyz789q", details.Code);
        Assert.Equal(2, _repository.Links.Count);
        Assert.Equal(0, _repository.ReplaceAttempts);
    }

    private LinkService CreateService(DateTime now, params string[] codes) => CreateService(now, _repository, codes);

    private static LinkService CreateService(DateTime now, IShortLinkRepository repository, params string[] codes) => new(
        repository,
        new ScriptedCodeGenerator(codes.Length == 0 ? ["Unused0"] : codes),
        new FakeClickRecorder(),
        new FakeClickStatsReader(),
        Microsoft.Extensions.Options.Options.Create(new ShortLinkOptions { PublicBaseUrl = "http://localhost:5058" }),
        new FixedTimeProvider(new DateTimeOffset(now)),
        NullLogger<LinkService>.Instance);

    /// <summary>Returns a link that was already replaced, as a lookup racing another request would.</summary>
    private sealed class StaleLookupRepository(InMemoryShortLinkRepository inner, ShortLink stale)
        : IShortLinkRepository
    {
        public Task<ShortLink?> FindByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken) =>
            Task.FromResult<ShortLink?>(stale);

        public Task<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken) =>
            inner.TryAddAsync(link, cancellationToken);

        public Task<bool> TryReplaceExpiredAsync(
            ShortLink expired, ShortLink replacement, DateTime nowUtc, CancellationToken cancellationToken) =>
            inner.TryReplaceExpiredAsync(expired, replacement, nowUtc, cancellationToken);
    }
}