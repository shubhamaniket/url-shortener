using Microsoft.Extensions.Logging.Abstractions;

using UrlShortener.Application.Links;
using UrlShortener.Application.Options;
using UrlShortener.Domain.Links;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Application;

public class ExpiryTests
{
    private const string Destination = "https://example.com/flash-sale";
    private static readonly DateTime Now = new(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresAt = new(2026, 12, 31, 18, 29, 0, DateTimeKind.Utc);

    private readonly InMemoryShortLinkRepository _repository = new();
    private readonly ScriptedCodeGenerator _generator = new("Abc123x");
    private readonly FakeClickRecorder _recorder = new();

    [Fact]
    public async Task LinkWithoutExpiryNeverExpires()
    {
        var details = await CreateService(Now).CreateAsync(new CreateLinkCommand(Destination, null), CancellationToken.None);

        Assert.Null(details.ExpiresAtUtc);
        Assert.Null(Assert.Single(_repository.Links).ExpiresAtUtc);
    }

    [Fact]
    public async Task GeneratedCodeLinkStoresExpiryInUtc()
    {
        var details = await CreateService(Now).CreateAsync(
            new CreateLinkCommand(Destination, null, "2026-12-31T23:59:00+05:30"), CancellationToken.None);

        Assert.Equal(ExpiresAt, details.ExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, details.ExpiresAtUtc!.Value.Kind);
        Assert.Equal(ExpiresAt, Assert.Single(_repository.Links).ExpiresAtUtc);
    }

    [Fact]
    public async Task CustomAliasLinkStoresExpiry()
    {
        var details = await CreateService(Now).CreateAsync(
            new CreateLinkCommand(Destination, "flash-sale", "2026-12-31T18:29:00Z"), CancellationToken.None);

        Assert.Equal("flash-sale", details.Code);
        Assert.Equal(ExpiresAt, details.ExpiresAtUtc);
        Assert.Equal(ExpiresAt, Assert.Single(_repository.Links).ExpiresAtUtc);
    }

    [Theory]
    [InlineData("2026-12-31T23:59:00")]
    [InlineData("2026-10-11T09:30:00Z")]
    [InlineData("2020-01-01T00:00:00Z")]
    [InlineData("")]
    public async Task InvalidExpiryStoresNothing(string expiresAt)
    {
        var error = await Assert.ThrowsAsync<LinkValidationException>(() => CreateService(Now).CreateAsync(
            new CreateLinkCommand(Destination, "flash-sale", expiresAt), CancellationToken.None));

        Assert.Equal("expiresAt", error.Field);
        Assert.Empty(_repository.Links);
        Assert.Equal(0, _repository.AddAttempts);
        Assert.Equal(0, _generator.Calls);
    }

    [Fact]
    public async Task InvalidAliasIsReportedBeforeInvalidExpiry()
    {
        var error = await Assert.ThrowsAsync<LinkValidationException>(() => CreateService(Now).CreateAsync(
            new CreateLinkCommand(Destination, "ab", "2026-12-31T23:59:00"), CancellationToken.None));

        Assert.Equal("customAlias", error.Field);
    }

    [Fact]
    public async Task BeforeExpiryRedirectsAndRecordsOneClick()
    {
        SeedExpiringLink();

        var resolution = await CreateService(ExpiresAt.AddTicks(-1)).ResolveForRedirectAsync("Abc123x", CancellationToken.None);

        Assert.Equal(RedirectResolution.Found(Destination), resolution);
        Assert.Single(_recorder.RecordedLinkIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(86_400)]
    public async Task AtOrAfterExpiryIsExpiredAndRecordsNoClick(int secondsAfterExpiry)
    {
        SeedExpiringLink();

        var resolution = await CreateService(ExpiresAt.AddSeconds(secondsAfterExpiry))
            .ResolveForRedirectAsync("Abc123x", CancellationToken.None);

        Assert.Equal(RedirectResolution.Expired, resolution);
        Assert.False(resolution.IsFound);
        Assert.Empty(_recorder.RecordedLinkIds);
    }

    [Fact]
    public async Task ExpiredAliasMatchesInAnyCase()
    {
        _repository.Seed(ShortLink.CreateWithCustomAlias("Flash-Sale", Destination, Now, ExpiresAt));

        var resolution = await CreateService(ExpiresAt).ResolveForRedirectAsync("FLASH-SALE", CancellationToken.None);

        Assert.Equal(RedirectResolution.Expired, resolution);
    }

    [Fact]
    public async Task DetailsOfAnExpiredLinkAreStillReturned()
    {
        SeedExpiringLink();

        var details = await CreateService(ExpiresAt.AddDays(1)).GetDetailsAsync("Abc123x", CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(ExpiresAt, details.ExpiresAtUtc);
        Assert.Equal(Destination, details.OriginalUrl);
        Assert.Empty(_recorder.RecordedLinkIds);
    }

    private void SeedExpiringLink() =>
        _repository.Seed(ShortLink.CreateWithGeneratedCode("Abc123x", Destination, Now, ExpiresAt));

    private LinkService CreateService(DateTime now) => new(
        _repository,
        _generator,
        _recorder,
        Microsoft.Extensions.Options.Options.Create(new ShortLinkOptions { PublicBaseUrl = "http://localhost:5058" }),
        new FixedTimeProvider(new DateTimeOffset(now)),
        NullLogger<LinkService>.Instance);
}