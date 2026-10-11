using UrlShortener.Api.RateLimiting;

namespace UrlShortener.IntegrationTests.RateLimiting;

/// <summary>Budget settings rules (FR-010). In this project because the unit-test project does not reference the Api.</summary>
public class RateLimitingOptionsValidatorTests
{
    [Fact]
    public void DefaultsAreTheClarifiedBudgetsAndValid()
    {
        var options = new RateLimitingOptions();

        Assert.Equal((10, 60), (options.Create.PermitLimit, options.Create.WindowSeconds));
        Assert.Equal((60, 2), (options.Redirect.BurstLimit, options.Redirect.TokensPerSecond));
        Assert.Equal((60, 60), (options.Read.PermitLimit, options.Read.WindowSeconds));
        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData("Create:PermitLimit", 0)]
    [InlineData("Create:WindowSeconds", -1)]
    [InlineData("Redirect:BurstLimit", 0)]
    [InlineData("Redirect:TokensPerSecond", 0)]
    [InlineData("Read:PermitLimit", -5)]
    [InlineData("Read:WindowSeconds", 0)]
    public void EachSettingBelowOneIsRejectedByName(string setting, int value)
    {
        var options = new RateLimitingOptions();
        switch (setting)
        {
            case "Create:PermitLimit": options.Create.PermitLimit = value; break;
            case "Create:WindowSeconds": options.Create.WindowSeconds = value; break;
            case "Redirect:BurstLimit": options.Redirect.BurstLimit = value; break;
            case "Redirect:TokensPerSecond": options.Redirect.TokensPerSecond = value; break;
            case "Read:PermitLimit": options.Read.PermitLimit = value; break;
            default: options.Read.WindowSeconds = value; break;
        }

        Assert.Equal([$"RateLimiting:{setting} must be at least 1."], options.Validate());
    }
}