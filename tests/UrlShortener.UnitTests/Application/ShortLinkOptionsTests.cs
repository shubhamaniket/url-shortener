using UrlShortener.Application.Options;

namespace UrlShortener.UnitTests.Application;

public class ShortLinkOptionsTests
{
    [Theory]
    [InlineData("http://localhost:5058")]
    [InlineData("http://localhost:8080/")]
    [InlineData("https://sho.rt")]
    public void ValidateAcceptsAbsoluteHttpOrHttpsRootUrl(string value)
    {
        var options = new ShortLinkOptions { PublicBaseUrl = value };

        Assert.Null(options.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost:5058")]
    [InlineData("/relative")]
    [InlineData("ftp://files.example")]
    [InlineData("https://user:pass@sho.rt")]
    [InlineData("https://sho.rt/sub")]
    [InlineData("https://sho.rt/?x=1")]
    [InlineData("https://sho.rt/#top")]
    public void ValidateRejectsInvalidBaseUrl(string value)
    {
        var options = new ShortLinkOptions { PublicBaseUrl = value };

        var error = options.Validate();

        Assert.NotNull(error);
        Assert.Contains(ShortLinkOptions.SectionName, error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsMissingBaseUrl()
    {
        Assert.NotNull(new ShortLinkOptions().Validate());
    }

    [Theory]
    [InlineData("http://localhost:5058", "http://localhost:5058/")]
    [InlineData("http://localhost:5058/", "http://localhost:5058/")]
    [InlineData("HTTPS://Sho.RT", "https://sho.rt/")]
    public void BaseUriIsNormalizedWithTrailingSlash(string value, string expected)
    {
        var options = new ShortLinkOptions { PublicBaseUrl = value };

        Assert.Equal(new Uri(expected), options.BaseUri);
    }

    [Fact]
    public void BuildShortUrlAppendsCodeToBase()
    {
        var options = new ShortLinkOptions { PublicBaseUrl = "http://localhost:5058" };

        Assert.Equal("http://localhost:5058/Team-Offsite", options.BuildShortUrl("Team-Offsite"));
    }
}