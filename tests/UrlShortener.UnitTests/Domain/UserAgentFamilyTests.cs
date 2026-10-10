using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Domain;

public class UserAgentFamilyTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36", UserAgentFamily.Chrome)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/129.0.6668.69 Mobile/15E148 Safari/604.1", UserAgentFamily.Chrome)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36 Edg/129.0.0.0", UserAgentFamily.Edge)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14.6; rv:130.0) Gecko/20100101 Firefox/130.0", UserAgentFamily.Firefox)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Safari/605.1.15", UserAgentFamily.Safari)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Mobile/15E148 Safari/604.1", UserAgentFamily.Safari)]
    public void RecognisesBrowsers(string userAgent, string expected)
    {
        Assert.Equal(expected, UserAgentFamily.From(userAgent));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("Mozilla/5.0 (Linux; Android 6.0.1; Nexus 5X Build/MMB29P) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.6668.70 Mobile Safari/537.36 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("Slackbot-LinkExpanding 1.0 (+https://api.slack.com/robots)")]
    [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)")]
    [InlineData("WhatsApp/2.23.20.0")]
    [InlineData("Mozilla/5.0 (compatible; Yahoo! Slurp; http://help.yahoo.com/help/us/ysearch/slurp)")]
    [InlineData("SomeLinkPreview/1.0")]
    public void CrawlersAndLinkPreviewsAreBotsEvenWithBrowserTokens(string userAgent)
    {
        Assert.Equal(UserAgentFamily.Bot, UserAgentFamily.From(userAgent));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("curl/8.7.1")]
    [InlineData("PostmanRuntime/7.42.0")]
    public void MissingOrUnrecognisedIsOther(string? userAgent)
    {
        Assert.Equal(UserAgentFamily.Other, UserAgentFamily.From(userAgent));
    }

    [Fact]
    public void MatchingIgnoresCase()
    {
        Assert.Equal(UserAgentFamily.Firefox, UserAgentFamily.From("mozilla/5.0 firefox/130.0"));
    }

    [Fact]
    public void EveryFamilyFitsTheStoredLength()
    {
        string[] families = [UserAgentFamily.Bot, UserAgentFamily.Edge, UserAgentFamily.Firefox, UserAgentFamily.Chrome, UserAgentFamily.Safari, UserAgentFamily.Other];

        Assert.All(families, family => Assert.InRange(family.Length, 1, UserAgentFamily.MaxLength));
    }
}