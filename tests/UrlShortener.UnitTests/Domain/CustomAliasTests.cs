using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Domain;

public class CustomAliasTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("team-offsite")]
    [InlineData("Team_2026")]
    [InlineData("A-b_C-1")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123")]
    public void AcceptsValidAliasesKeepingCase(string input)
    {
        Assert.Equal(input, CustomAlias.Parse(input).Value);
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal("team-offsite", CustomAlias.Parse("  team-offsite ").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxyz01234")]
    public void RejectsLengthOutsideLimits(string input)
    {
        Assert.Equal("The alias must be 3–30 characters long.", AssertRejected(input).Message);
    }

    [Theory]
    [InlineData("my alias")]
    [InlineData("über")]
    [InlineData("a.b")]
    [InlineData("a/b")]
    [InlineData("a?b")]
    [InlineData("a%2Fb")]
    [InlineData("<script>")]
    public void RejectsCharactersOutsideTheAllowedSet(string input)
    {
        Assert.Equal("The alias may only contain letters, digits, '-' and '_'.", AssertRejected(input).Message);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("API")]
    [InlineData("Health")]
    [InlineData("swagger")]
    [InlineData("admin")]
    [InlineData("static")]
    [InlineData("ASSETS")]
    public void RejectsReservedWordsInAnyCase(string input)
    {
        Assert.Equal($"The alias '{input}' is reserved.", AssertRejected(input).Message);
    }

    [Fact]
    public void ReservedWordInsideALongerAliasIsAllowed()
    {
        Assert.Equal("api-docs", CustomAlias.Parse("api-docs").Value);
    }

    private static LinkValidationException AssertRejected(string input)
    {
        var error = Assert.Throws<LinkValidationException>(() => CustomAlias.Parse(input));
        Assert.Equal("customAlias", error.Field);
        return error;
    }
}