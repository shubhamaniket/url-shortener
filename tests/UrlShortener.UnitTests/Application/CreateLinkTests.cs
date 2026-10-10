using Microsoft.Extensions.Logging.Abstractions;

using UrlShortener.Application.Links;
using UrlShortener.Application.Options;
using UrlShortener.Domain.Links;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Application;

public class CreateLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 1, 30, 0, TimeSpan.Zero);

    private readonly InMemoryShortLinkRepository _repository = new();

    [Fact]
    public async Task CreatesLinkWithGeneratedCode()
    {
        var service = CreateService(new ScriptedCodeGenerator("Abc123x"));

        var details = await service.CreateAsync(new CreateLinkCommand("https://example.com/a?x=1", null), CancellationToken.None);

        Assert.Equal("Abc123x", details.Code);
        Assert.Equal("http://localhost:5058/Abc123x", details.ShortUrl);
        Assert.Equal("https://example.com/a?x=1", details.OriginalUrl);
        Assert.Equal(Now.UtcDateTime, details.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, details.CreatedAtUtc.Kind);
        Assert.Equal(0, details.ClickCount);

        var stored = Assert.Single(_repository.Links);
        Assert.Equal("Abc123x", stored.Code);
        Assert.False(stored.IsCustomAlias);
    }

    [Fact]
    public async Task RetriesWithNewCodeAfterCollision()
    {
        _repository.Seed(ShortLink.CreateWithGeneratedCode("Abc123x", "https://example.com/old", Now.UtcDateTime));
        var generator = new ScriptedCodeGenerator("Abc123x", "Xyz789q");
        var service = CreateService(generator);

        var details = await service.CreateAsync(new CreateLinkCommand("https://example.com/new", null), CancellationToken.None);

        Assert.Equal("Xyz789q", details.Code);
        Assert.Equal(2, generator.Calls);
    }

    [Fact]
    public async Task GivesUpAfterFiveCollisions()
    {
        _repository.Seed(ShortLink.CreateWithGeneratedCode("Abc123x", "https://example.com/old", Now.UtcDateTime));
        var generator = new ScriptedCodeGenerator("Abc123x");
        var service = CreateService(generator);

        var error = await Assert.ThrowsAsync<CodeGenerationFailedException>(() =>
            service.CreateAsync(new CreateLinkCommand("https://example.com/new", null), CancellationToken.None));

        Assert.Equal(5, error.Attempts);
        Assert.Equal(5, generator.Calls);
        Assert.Equal(5, _repository.AddAttempts);
        Assert.Single(_repository.Links);
    }

    [Fact]
    public async Task GeneratedCodeMatchingExistingAliasIgnoringCaseIsACollision()
    {
        _repository.Seed(ShortLink.CreateWithCustomAlias("abc123x", "https://example.com/alias", Now.UtcDateTime));
        var generator = new ScriptedCodeGenerator("ABC123X", "Xyz789q");
        var service = CreateService(generator);

        var details = await service.CreateAsync(new CreateLinkCommand("https://example.com/new", null), CancellationToken.None);

        Assert.Equal("Xyz789q", details.Code);
        Assert.Equal(2, _repository.Links.Count);
    }

    [Fact]
    public async Task SameUrlTwiceCreatesTwoLinks()
    {
        var service = CreateService(new ScriptedCodeGenerator("Abc123x", "Xyz789q"));

        var first = await service.CreateAsync(new CreateLinkCommand("https://example.com/a", null), CancellationToken.None);
        var second = await service.CreateAsync(new CreateLinkCommand("https://example.com/a", null), CancellationToken.None);

        Assert.NotEqual(first.Code, second.Code);
        Assert.Equal(2, _repository.Links.Count);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://LOCALHOST:5058/x")]
    [InlineData("")]
    public async Task InvalidUrlIsRejectedBeforeAnythingIsGeneratedOrStored(string url)
    {
        var generator = new ScriptedCodeGenerator("Abc123x");
        var service = CreateService(generator);

        var error = await Assert.ThrowsAsync<LinkValidationException>(() =>
            service.CreateAsync(new CreateLinkCommand(url, null), CancellationToken.None));

        Assert.Equal("url", error.Field);
        Assert.Equal(0, generator.Calls);
        Assert.Equal(0, _repository.AddAttempts);
    }

    private LinkService CreateService(ScriptedCodeGenerator generator) => new(
        _repository,
        generator,
        new FakeClickRecorder(),
        Microsoft.Extensions.Options.Options.Create(new ShortLinkOptions { PublicBaseUrl = "http://localhost:5058" }),
        new FixedTimeProvider(Now),
        NullLogger<LinkService>.Instance);
}