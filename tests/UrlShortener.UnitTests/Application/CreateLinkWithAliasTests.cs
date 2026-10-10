using Microsoft.Extensions.Logging.Abstractions;

using UrlShortener.Application.Links;
using UrlShortener.Application.Options;
using UrlShortener.Domain.Links;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Application;

public class CreateLinkWithAliasTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 2, 0, 0, TimeSpan.Zero);

    private readonly InMemoryShortLinkRepository _repository = new();
    private readonly ScriptedCodeGenerator _generator = new("Abc123x");

    [Fact]
    public async Task UsesAliasAsCodeKeepingItsCase()
    {
        var details = await CreateService().CreateAsync(
            new CreateLinkCommand("https://example.com/offsite", "Team-Offsite"), CancellationToken.None);

        Assert.Equal("Team-Offsite", details.Code);
        Assert.Equal("http://localhost:5058/Team-Offsite", details.ShortUrl);
        Assert.Equal(0, _generator.Calls);
        var stored = Assert.Single(_repository.Links);
        Assert.True(stored.IsCustomAlias);
        Assert.Equal("team-offsite", stored.NormalizedCode);
    }

    [Fact]
    public async Task TrimsAlias()
    {
        var details = await CreateService().CreateAsync(
            new CreateLinkCommand("https://example.com/offsite", "  team-offsite "), CancellationToken.None);

        Assert.Equal("team-offsite", details.Code);
    }

    [Fact]
    public async Task AliasTakenInAnotherCaseThrowsConflictWithoutRetry()
    {
        _repository.Seed(ShortLink.CreateWithCustomAlias("team-offsite", "https://example.com/first", Now.UtcDateTime));

        var error = await Assert.ThrowsAsync<AliasAlreadyExistsException>(() => CreateService().CreateAsync(
            new CreateLinkCommand("https://example.com/second", "Team-Offsite"), CancellationToken.None));

        Assert.Equal("Team-Offsite", error.Alias);
        Assert.Equal(1, _repository.AddAttempts);
        Assert.Equal("https://example.com/first", Assert.Single(_repository.Links).OriginalUrl);
    }

    [Fact]
    public async Task AliasMatchingExistingGeneratedCodeIgnoringCaseIsAConflict()
    {
        _repository.Seed(ShortLink.CreateWithGeneratedCode("Abc123x", "https://example.com/generated", Now.UtcDateTime));

        await Assert.ThrowsAsync<AliasAlreadyExistsException>(() => CreateService().CreateAsync(
            new CreateLinkCommand("https://example.com/alias", "abc123x"), CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("my alias")]
    [InlineData("API")]
    public async Task InvalidAliasIsRejectedBeforeStoring(string alias)
    {
        var error = await Assert.ThrowsAsync<LinkValidationException>(() => CreateService().CreateAsync(
            new CreateLinkCommand("https://example.com/a", alias), CancellationToken.None));

        Assert.Equal("customAlias", error.Field);
        Assert.Equal(0, _repository.AddAttempts);
    }

    [Fact]
    public async Task InvalidUrlIsReportedBeforeInvalidAlias()
    {
        var error = await Assert.ThrowsAsync<LinkValidationException>(() => CreateService().CreateAsync(
            new CreateLinkCommand("javascript:alert(1)", "ab"), CancellationToken.None));

        Assert.Equal("url", error.Field);
    }

    [Fact]
    public async Task NullAliasUsesGeneratedCode()
    {
        var details = await CreateService().CreateAsync(
            new CreateLinkCommand("https://example.com/a", null), CancellationToken.None);

        Assert.Equal("Abc123x", details.Code);
        Assert.False(Assert.Single(_repository.Links).IsCustomAlias);
    }

    private LinkService CreateService() => new(
        _repository,
        _generator,
        new FakeClickRecorder(),
        Microsoft.Extensions.Options.Options.Create(new ShortLinkOptions { PublicBaseUrl = "http://localhost:5058" }),
        new FixedTimeProvider(Now),
        NullLogger<LinkService>.Instance);
}