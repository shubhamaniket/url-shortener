using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Domain;

public class ReferrerHostTests
{
    [Theory]
    [InlineData("https://News.Example/a?u=42", "news.example")]
    [InlineData("https://user:pass@site.example/x", "site.example")]
    [InlineData("http://blog.example:8080/post#comments", "blog.example")]
    [InlineData("  https://news.example/  ", "news.example")]
    [InlineData("https://bücher.example/", "xn--bcher-kva.example")]
    public void KeepsOnlyTheLowerCasedHost(string referer, string expected)
    {
        Assert.Equal(expected, ReferrerHost.From(referer));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("android-app://com.x")]
    [InlineData("ftp://f.example")]
    public void MissingInvalidOrNonWebReferrerIsDirect(string? referer)
    {
        Assert.Null(ReferrerHost.From(referer));
    }

    [Fact]
    public void HostLongerThanMaxLengthIsDirect()
    {
        var label = new string('a', 60);
        var host = string.Join('.', label, label, label, label, label, "example");

        Assert.True(host.Length > ReferrerHost.MaxLength);
        Assert.Null(ReferrerHost.From($"https://{host}/"));
    }
}