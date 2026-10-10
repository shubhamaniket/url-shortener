using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Domain;

public class ClickEventTests
{
    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";

    private static readonly DateTime OccurredAt = new(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateSetsLinkAndTime()
    {
        var click = ClickEvent.Create(42, OccurredAt, referer: null, userAgent: null);

        Assert.Equal(42, click.ShortLinkId);
        Assert.Equal(OccurredAt, click.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, click.OccurredAtUtc.Kind);
        Assert.Equal(0, click.Id);
    }

    [Fact]
    public void StoresOnlyTheReferrerHost()
    {
        var click = ClickEvent.Create(1, OccurredAt, "https://user:secret@News.Example/article?u=42#top", ChromeUserAgent);

        Assert.Equal("news.example", click.ReferrerHost);
    }

    [Fact]
    public void StoresOnlyTheUserAgentFamily()
    {
        var click = ClickEvent.Create(1, OccurredAt, referer: null, ChromeUserAgent);

        Assert.Equal(UserAgentFamily.Chrome, click.UserAgentFamily);
    }

    [Fact]
    public void MissingHeadersAreDirectAndOther()
    {
        var click = ClickEvent.Create(1, OccurredAt, referer: null, userAgent: null);

        Assert.Null(click.ReferrerHost);
        Assert.Equal(UserAgentFamily.Other, click.UserAgentFamily);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void TimeMustBeUtc(DateTimeKind kind)
    {
        var time = DateTime.SpecifyKind(OccurredAt, kind);

        var error = Assert.Throws<ArgumentException>(() => ClickEvent.Create(1, time, referer: null, userAgent: null));

        Assert.Equal("occurredAtUtc", error.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LinkIdMustBePositive(long shortLinkId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClickEvent.Create(shortLinkId, OccurredAt, referer: null, userAgent: null));
    }
}