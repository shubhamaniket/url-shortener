using Microsoft.Extensions.Logging.Abstractions;

using UrlShortener.Application.Links;
using UrlShortener.Application.Options;
using UrlShortener.Domain.Links;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Application;

public class GetLinkDetailsTests
{
    private static readonly DateTime CreatedAt = new(2026, 10, 11, 2, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryShortLinkRepository _repository = new();
    private readonly FakeClickRecorder _recorder = new();

    public GetLinkDetailsTests()
    {
        _repository.Seed(ShortLink.CreateWithGeneratedCode("Abc123x", "https://example.com/a", CreatedAt));
        _repository.Seed(ShortLink.CreateWithCustomAlias("Team-Offsite", "https://example.com/offsite", CreatedAt));
    }

    [Fact]
    public async Task ReturnsDetailsWithoutRecordingAClick()
    {
        var details = await CreateService().GetDetailsAsync("Abc123x", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("Abc123x", details.Code);
        Assert.Equal("http://localhost:5058/Abc123x", details.ShortUrl);
        Assert.Equal("https://example.com/a", details.OriginalUrl);
        Assert.Equal(CreatedAt, details.CreatedAtUtc);
        Assert.Equal(0, details.ClickCount);
        Assert.Empty(_recorder.RecordedLinkIds);
    }

    [Theory]
    [InlineData("team-offsite")]
    [InlineData("TEAM-OFFSITE")]
    public async Task FindsAliasInAnyCaseAndReturnsOriginalCase(string code)
    {
        var details = await CreateService().GetDetailsAsync(code, CancellationToken.None);

        Assert.Equal("Team-Offsite", details?.Code);
    }

    [Theory]
    [InlineData("abc123x")]
    [InlineData("zzzzzzz")]
    public async Task UnknownCodeOrGeneratedCodeInWrongCaseReturnsNull(string code)
    {
        Assert.Null(await CreateService().GetDetailsAsync(code, CancellationToken.None));
        Assert.Empty(_recorder.RecordedLinkIds);
    }

    private LinkService CreateService() => new(
        _repository,
        new ScriptedCodeGenerator("Unused0"),
        _recorder,
        Microsoft.Extensions.Options.Options.Create(new ShortLinkOptions { PublicBaseUrl = "http://localhost:5058" }),
        new FixedTimeProvider(new DateTimeOffset(CreatedAt)),
        NullLogger<LinkService>.Instance);
}