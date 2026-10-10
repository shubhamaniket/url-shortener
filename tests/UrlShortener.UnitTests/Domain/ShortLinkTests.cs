using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Domain;

public class ShortLinkTests
{
    private const string Url = "https://example.com/some/path";
    private static readonly DateTime CreatedAt = new(2026, 10, 10, 13, 45, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateWithGeneratedCodeSetsAllFields()
    {
        var link = ShortLink.CreateWithGeneratedCode("Abc123x", Url, CreatedAt);

        Assert.Equal("Abc123x", link.Code);
        Assert.Equal("abc123x", link.NormalizedCode);
        Assert.False(link.IsCustomAlias);
        Assert.Equal(Url, link.OriginalUrl);
        Assert.Equal(CreatedAt, link.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, link.CreatedAtUtc.Kind);
        Assert.Equal(0, link.ClickCount);
    }

    [Fact]
    public void CreateWithCustomAliasKeepsOriginalCase()
    {
        var link = ShortLink.CreateWithCustomAlias("Team-Offsite", Url, CreatedAt);

        Assert.Equal("Team-Offsite", link.Code);
        Assert.Equal("team-offsite", link.NormalizedCode);
        Assert.True(link.IsCustomAlias);
        Assert.Equal(0, link.ClickCount);
    }

    [Theory]
    [InlineData("team-offsite")]
    [InlineData("TEAM-OFFSITE")]
    [InlineData("Team-Offsite")]
    public void CustomAliasMatchesInAnyCase(string requested)
    {
        var link = ShortLink.CreateWithCustomAlias("team-offsite", Url, CreatedAt);

        Assert.True(link.Matches(requested));
    }

    [Theory]
    [InlineData("Abc123x", true)]
    [InlineData("abc123x", false)]
    [InlineData("ABC123X", false)]
    public void GeneratedCodeMatchesExactCaseOnly(string requested, bool expected)
    {
        var link = ShortLink.CreateWithGeneratedCode("Abc123x", Url, CreatedAt);

        Assert.Equal(expected, link.Matches(requested));
    }

    [Fact]
    public void DifferentCodeDoesNotMatch()
    {
        var generated = ShortLink.CreateWithGeneratedCode("Abc123x", Url, CreatedAt);
        var alias = ShortLink.CreateWithCustomAlias("team-offsite", Url, CreatedAt);

        Assert.False(generated.Matches("Abc123y"));
        Assert.False(alias.Matches("team-offsite2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Abc123")]
    [InlineData("Abc123xy")]
    [InlineData("Abc-23x")]
    [InlineData("Abc 23x")]
    [InlineData("Abc123é")]
    public void CreateWithGeneratedCodeRejectsInvalidCode(string code)
    {
        Assert.ThrowsAny<ArgumentException>(() => ShortLink.CreateWithGeneratedCode(code, Url, CreatedAt));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(31)]
    public void CreateWithCustomAliasRejectsLengthOutsideLimits(int length)
    {
        var alias = new string('a', length);

        Assert.ThrowsAny<ArgumentException>(() => ShortLink.CreateWithCustomAlias(alias, Url, CreatedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsMissingUrl(string url)
    {
        Assert.ThrowsAny<ArgumentException>(() => ShortLink.CreateWithGeneratedCode("Abc123x", url, CreatedAt));
    }

    [Fact]
    public void CreateRejectsUrlLongerThanLimit()
    {
        var url = "https://example.com/" + new string('a', ShortLink.MaxUrlLength);

        Assert.ThrowsAny<ArgumentException>(() => ShortLink.CreateWithGeneratedCode("Abc123x", url, CreatedAt));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void CreateRejectsNonUtcCreationTime(DateTimeKind kind)
    {
        var createdAt = DateTime.SpecifyKind(CreatedAt, kind);

        Assert.Throws<ArgumentException>(() => ShortLink.CreateWithGeneratedCode("Abc123x", Url, createdAt));
    }

    [Fact]
    public void LinkWithoutExpiryNeverExpires()
    {
        var link = ShortLink.CreateWithGeneratedCode("Abc123x", Url, CreatedAt);

        Assert.Null(link.ExpiresAtUtc);
        Assert.False(link.IsExpiredAt(DateTime.MaxValue.ToUniversalTime()));
    }

    [Fact]
    public void LinkExpiresExactlyAtAndAfterItsExpiryTime()
    {
        var expiresAt = CreatedAt.AddHours(1);
        var link = ShortLink.CreateWithCustomAlias("team-offsite", Url, CreatedAt, expiresAt);

        Assert.Equal(expiresAt, link.ExpiresAtUtc);
        Assert.False(link.IsExpiredAt(expiresAt.AddTicks(-1)));
        Assert.True(link.IsExpiredAt(expiresAt));
        Assert.True(link.IsExpiredAt(expiresAt.AddDays(1)));
    }

    [Fact]
    public void GeneratedCodeLinkCanHaveExpiry()
    {
        var link = ShortLink.CreateWithGeneratedCode("Abc123x", Url, CreatedAt, CreatedAt.AddMinutes(5));

        Assert.Equal(CreatedAt.AddMinutes(5), link.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void ExpiryMustBeAfterCreation(int secondsAfterCreation)
    {
        Assert.Throws<ArgumentException>(() =>
            ShortLink.CreateWithGeneratedCode("Abc123x", Url, CreatedAt, CreatedAt.AddSeconds(secondsAfterCreation)));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ExpiryMustBeUtc(DateTimeKind kind)
    {
        var expiresAt = DateTime.SpecifyKind(CreatedAt.AddHours(1), kind);

        Assert.Throws<ArgumentException>(() => ShortLink.CreateWithGeneratedCode("Abc123x", Url, CreatedAt, expiresAt));
    }
}