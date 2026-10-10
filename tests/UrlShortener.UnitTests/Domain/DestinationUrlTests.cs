using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Domain;

public class DestinationUrlTests
{
    private const string OwnHost = "localhost";

    [Theory]
    [InlineData("https://example.com/some/long/path?x=1", "https://example.com/some/long/path?x=1")]
    [InlineData("http://example.com/a", "http://example.com/a")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("  https://example.com/a  ", "https://example.com/a")]
    [InlineData("HTTPS://Example.COM/Path", "https://example.com/Path")]
    [InlineData("https://localhost.example.com/a", "https://localhost.example.com/a")]
    public void AcceptsAbsoluteHttpAndHttpsUrls(string input, string expected)
    {
        var url = DestinationUrl.Parse(input, OwnHost);

        Assert.Equal(expected, url.Value);
    }

    [Fact]
    public void AcceptsUrlOfExactlyMaxLength()
    {
        var prefix = "https://example.com/";
        var input = prefix + new string('a', ShortLink.MaxUrlLength - prefix.Length);

        Assert.Equal(ShortLink.MaxUrlLength, DestinationUrl.Parse(input, OwnHost).Value.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsMissingUrl(string? input)
    {
        var error = AssertRejected(input);

        Assert.Equal("A URL is required.", error.Message);
    }

    [Fact]
    public void RejectsUrlLongerThanMaxLength()
    {
        var prefix = "https://example.com/";
        var input = prefix + new string('a', ShortLink.MaxUrlLength - prefix.Length + 1);

        var error = AssertRejected(input);

        Assert.Contains("2048", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://files.example.com/a")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("/some/path")]
    [InlineData("not a url")]
    [InlineData("http:///path")]
    public void RejectsNonHttpOrMalformedUrls(string input)
    {
        var error = AssertRejected(input);

        Assert.DoesNotContain("Did you mean", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("www.example.com/page", "https://www.example.com/page")]
    [InlineData("example.com/page", "https://example.com/page")]
    [InlineData("www.example.com:8080/page", "https://www.example.com:8080/page")]
    [InlineData("localhost:8080", "https://localhost:8080")]
    public void RejectsMissingSchemeWithHint(string input, string suggestion)
    {
        var error = AssertRejected(input);

        Assert.Equal(
            $"The URL must start with http:// or https://. Did you mean {suggestion}?",
            error.Message);
    }

    [Theory]
    [InlineData("https://google.com@evil.example/login")]
    [InlineData("https://user:pass@site.example")]
    [InlineData("http://user@site.example/a")]
    public void RejectsUserInfo(string input)
    {
        var error = AssertRejected(input);

        Assert.Equal("The URL must not contain a username or password.", error.Message);
    }

    [Theory]
    [InlineData("http://localhost:5058/abc1234")]
    [InlineData("HTTP://LOCALHOST:5058/x")]
    [InlineData("https://localhost/x")]
    [InlineData("http://LocalHost:9999")]
    public void RejectsOwnHostInAnyCaseSchemeOrPort(string input)
    {
        var error = AssertRejected(input);

        Assert.Equal("The URL must not point to this URL shortener.", error.Message);
    }

    [Theory]
    [InlineData(@"https:\\example.com\a")]
    [InlineData(@"https://example.com/a\b")]
    public void BackslashesAreNeverStoredRaw(string input)
    {
        // Browsers read "\" as "/" in http(s) URLs, while System.Uri's handling differs by platform
        // (Windows converts, Unix rejects or escapes). Whatever the platform does, a raw backslash
        // must never be stored and sent in a redirect, or the validated host and the browser's
        // destination could differ.
        DestinationUrl url;
        try
        {
            url = DestinationUrl.Parse(input, OwnHost);
        }
        catch (LinkValidationException)
        {
            return;
        }

        Assert.DoesNotContain('\\', url.Value);
        Assert.StartsWith("https://example.com/", url.Value, StringComparison.Ordinal);
    }

    private static LinkValidationException AssertRejected(string? input)
    {
        var error = Assert.Throws<LinkValidationException>(() => DestinationUrl.Parse(input, OwnHost));
        Assert.Equal("url", error.Field);
        return error;
    }
}