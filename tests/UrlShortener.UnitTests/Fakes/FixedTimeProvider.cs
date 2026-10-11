namespace UrlShortener.UnitTests.Fakes;

/// <summary>
/// A clock stopped at <paramref name="now"/>. The local time zone can be pinned so code that wrongly
/// uses local time fails on every machine, not only on machines outside UTC.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => localTimeZone ?? base.LocalTimeZone;
}