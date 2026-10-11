using System.Globalization;

using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Domain;

public class ExpiryTimeTests
{
    private const string FormatMessage =
        "The expiry must be a date and time with a time zone, e.g. 2026-12-31T23:59:00+05:30.";

    private const string ZoneMessage =
        "The expiry needs a time zone, e.g. 2026-12-31T23:59:00Z or 2026-12-31T23:59:00+05:30.";

    private static readonly DateTime Now = new(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-12-31T23:59:00+05:30", "2026-12-31T18:29:00")]
    [InlineData("2026-12-31T23:59:00Z", "2026-12-31T23:59:00")]
    [InlineData("2026-12-31T23:59:00-08:00", "2027-01-01T07:59:00")]
    [InlineData("2026-12-31T23:59Z", "2026-12-31T23:59:00")]
    [InlineData("2026-12-31T23:59:00.5+00:00", "2026-12-31T23:59:00.5")]
    [InlineData("  2026-12-31T23:59:00Z  ", "2026-12-31T23:59:00")]
    public void AcceptsZonedFutureTimesAsUtc(string input, string expectedUtc)
    {
        var expiry = ExpiryTime.Parse(input, Now);

        Assert.Equal(DateTime.Parse(expectedUtc, CultureInfo.InvariantCulture), expiry.Utc);
        Assert.Equal(DateTimeKind.Utc, expiry.Utc.Kind);
    }

    [Fact]
    public void AcceptsOneTickAfterNow()
    {
        // 09:30:00.0000001 UTC written in India time.
        var expiry = ExpiryTime.Parse("2026-10-11T15:00:00.0000001+05:30", Now);

        Assert.Equal(Now.AddTicks(1), expiry.Utc);
    }

    [Theory]
    [InlineData("2026-12-31T23:59:00")]
    [InlineData("2026-12-31T23:59")]
    [InlineData("2026-12-31T23:59:00.123")]
    public void RejectsTimesWithoutZone(string input)
    {
        Assert.Equal(ZoneMessage, AssertRejected(input).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12/10/2026")]
    [InlineData("tomorrow")]
    [InlineData("2026-12-31")]
    [InlineData("2026-12-31 23:59:00Z")]
    [InlineData("2026-12-31t23:59:00z")]
    [InlineData("2026-02-30T10:00:00Z")]
    [InlineData("2026-12-31T24:00:00Z")]
    [InlineData("2026-12-31T23:59:00+15:00")]
    [InlineData("2026-12-31T23:59:00+0530")]
    [InlineData("1798761540")]
    public void RejectsOtherFormats(string? input)
    {
        Assert.Equal(FormatMessage, AssertRejected(input).Message);
    }

    [Theory]
    [InlineData("2020-01-01T00:00:00Z")]
    [InlineData("2026-10-11T09:30:00Z")]
    [InlineData("2026-10-11T15:00:00+05:30")]
    [InlineData("2026-10-11T09:29:59.9999999Z")]
    public void RejectsNowAndThePast(string input)
    {
        Assert.Equal("The expiry must be in the future.", AssertRejected(input).Message);
    }

    [Fact]
    public void CurrentTimeMustBeUtc()
    {
        var local = DateTime.SpecifyKind(Now, DateTimeKind.Local);

        var error = Assert.Throws<ArgumentException>(() => ExpiryTime.Parse("2026-12-31T23:59:00Z", local));

        Assert.Equal("nowUtc", error.ParamName);
    }

    private static LinkValidationException AssertRejected(string? input)
    {
        var error = Assert.Throws<LinkValidationException>(() => ExpiryTime.Parse(input, Now));
        Assert.Equal("expiresAt", error.Field);
        return error;
    }
}