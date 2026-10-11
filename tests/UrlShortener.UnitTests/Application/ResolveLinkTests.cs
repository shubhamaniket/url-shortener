using Microsoft.Extensions.Logging;

using UrlShortener.Application.Links;
using UrlShortener.Application.Options;
using UrlShortener.Domain.Links;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Application;

public class ResolveLinkTests
{
    private const string Destination = "https://example.com/private?token=abc";
    private static readonly DateTime CreatedAt = new(2026, 10, 11, 1, 30, 0, DateTimeKind.Utc);

    private readonly InMemoryShortLinkRepository _repository = new();
    private readonly ListLogger<LinkService> _logger = new();

    public ResolveLinkTests()
    {
        _repository.Seed(ShortLink.CreateWithGeneratedCode("Abc123x", Destination, CreatedAt));
        _repository.Seed(ShortLink.CreateWithCustomAlias("team-offsite", "https://example.com/offsite", CreatedAt));
    }

    [Fact]
    public async Task ReturnsOriginalUrlAndRecordsOneClick()
    {
        var recorder = new FakeClickRecorder();

        var resolution = await CreateService(recorder).ResolveForRedirectAsync("Abc123x", ClickContext.None, CancellationToken.None);

        Assert.Equal(RedirectResolution.Found(Destination), resolution);
        Assert.Single(recorder.RecordedLinkIds);
    }

    [Theory]
    [InlineData("team-offsite")]
    [InlineData("TEAM-OFFSITE")]
    public async Task ResolvesCustomAliasInAnyCase(string code)
    {
        var resolution = await CreateService(new FakeClickRecorder()).ResolveForRedirectAsync(code, ClickContext.None, CancellationToken.None);

        Assert.Equal(RedirectResolution.Found("https://example.com/offsite"), resolution);
    }

    [Theory]
    [InlineData("zzzzzzz")]
    [InlineData("abc123x")]
    [InlineData("ABC123X")]
    public async Task UnknownCodeOrGeneratedCodeInWrongCaseIsNotFoundWithoutRecording(string code)
    {
        var recorder = new FakeClickRecorder();

        var resolution = await CreateService(recorder).ResolveForRedirectAsync(code, ClickContext.None, CancellationToken.None);

        Assert.Equal(RedirectResolution.NotFound, resolution);
        Assert.Empty(recorder.RecordedLinkIds);
    }

    [Fact]
    public async Task RecorderFailureStillReturnsUrlAndIsLoggedWithoutTheDestination()
    {
        var recorder = new FakeClickRecorder(new InvalidOperationException("database is locked"));

        var resolution = await CreateService(recorder).ResolveForRedirectAsync("Abc123x", ClickContext.None, CancellationToken.None);

        Assert.Equal(RedirectResolution.Found(Destination), resolution);
        var entry = Assert.Single(_logger.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Contains("Abc123x", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token=abc", entry.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task RecorderThrowingCancellationStillReturnsUrl()
    {
        var recorder = new FakeClickRecorder(new OperationCanceledException("recorder shut down"));

        var resolution = await CreateService(recorder).ResolveForRedirectAsync("Abc123x", ClickContext.None, CancellationToken.None);

        Assert.Equal(RedirectResolution.Found(Destination), resolution);
    }

    [Fact]
    public async Task CancelledRequestPropagatesCancellationAndRecordsNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var recorder = new FakeClickRecorder();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(recorder).ResolveForRedirectAsync("Abc123x", ClickContext.None, cancellation.Token));
        Assert.Empty(recorder.Recorded);
    }

    [Fact]
    public async Task QueuesOnlyTheReducedClickAtTheCurrentTime()
    {
        var recorder = new FakeClickRecorder();
        var context = new ClickContext(
            "https://user:secret@News.Example/article?u=42",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 14.6; rv:130.0) Gecko/20100101 Firefox/130.0");

        await CreateService(recorder).ResolveForRedirectAsync("Abc123x", context, CancellationToken.None);

        var click = Assert.Single(recorder.Recorded);
        Assert.Equal(_repository.Links.Single(link => link.Code == "Abc123x").Id, click.ShortLinkId);
        Assert.Equal(CreatedAt, click.OccurredAtUtc);
        Assert.Equal("news.example", click.ReferrerHost);
        Assert.Equal(UserAgentFamily.Firefox, click.UserAgentFamily);
    }

    private LinkService CreateService(FakeClickRecorder recorder) => new(
        _repository,
        new ScriptedCodeGenerator("Unused0"),
        recorder,
        new FakeClickStatsReader(),
        Microsoft.Extensions.Options.Options.Create(new ShortLinkOptions { PublicBaseUrl = "http://localhost:5058" }),
        new FixedTimeProvider(new DateTimeOffset(CreatedAt)),
        _logger);
}