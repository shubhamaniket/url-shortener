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

        var url = await CreateService(recorder).ResolveForRedirectAsync("Abc123x", CancellationToken.None);

        Assert.Equal(Destination, url);
        Assert.Single(recorder.RecordedLinkIds);
    }

    [Theory]
    [InlineData("team-offsite")]
    [InlineData("TEAM-OFFSITE")]
    public async Task ResolvesCustomAliasInAnyCase(string code)
    {
        var url = await CreateService(new FakeClickRecorder()).ResolveForRedirectAsync(code, CancellationToken.None);

        Assert.Equal("https://example.com/offsite", url);
    }

    [Theory]
    [InlineData("zzzzzzz")]
    [InlineData("abc123x")]
    [InlineData("ABC123X")]
    public async Task UnknownCodeOrGeneratedCodeInWrongCaseReturnsNullWithoutRecording(string code)
    {
        var recorder = new FakeClickRecorder();

        var url = await CreateService(recorder).ResolveForRedirectAsync(code, CancellationToken.None);

        Assert.Null(url);
        Assert.Empty(recorder.RecordedLinkIds);
    }

    [Fact]
    public async Task RecorderFailureStillReturnsUrlAndIsLoggedWithoutTheDestination()
    {
        var recorder = new FakeClickRecorder(new InvalidOperationException("database is locked"));

        var url = await CreateService(recorder).ResolveForRedirectAsync("Abc123x", CancellationToken.None);

        Assert.Equal(Destination, url);
        var entry = Assert.Single(_logger.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Contains("Abc123x", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token=abc", entry.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task RecorderTimeoutThatIsNotTheRequestCancellationStillReturnsUrl()
    {
        var recorder = new FakeClickRecorder(new OperationCanceledException("recorder timed out"));

        var url = await CreateService(recorder).ResolveForRedirectAsync("Abc123x", CancellationToken.None);

        Assert.Equal(Destination, url);
    }

    [Fact]
    public async Task CancelledRequestPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var recorder = new FakeClickRecorder(new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(recorder).ResolveForRedirectAsync("Abc123x", cancellation.Token));
    }

    private LinkService CreateService(FakeClickRecorder recorder) => new(
        _repository,
        new ScriptedCodeGenerator("Unused0"),
        recorder,
        Microsoft.Extensions.Options.Options.Create(new ShortLinkOptions { PublicBaseUrl = "http://localhost:5058" }),
        new FixedTimeProvider(new DateTimeOffset(CreatedAt)),
        _logger);
}