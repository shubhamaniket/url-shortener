using System.Globalization;
using System.Text.RegularExpressions;

namespace UrlShortener.Domain.Links;

/// <summary>
/// A validated link expiry time (FR-002, FR-002a). The input must name its time zone, so a value
/// like "2026-12-31T23:59:00" is never silently read as server-local time; <see cref="Utc"/> is
/// the same instant in UTC.
/// </summary>
public sealed partial class ExpiryTime
{
    public const string Field = "expiresAt";

    private const string FormatMessage =
        "The expiry must be a date and time with a time zone, e.g. 2026-12-31T23:59:00+05:30.";

    private ExpiryTime(DateTime utc)
    {
        Utc = utc;
    }

    public DateTime Utc { get; }

    /// <summary>
    /// Applies the rules in order (data-model.md, ExpiryTime) and throws
    /// <see cref="LinkValidationException"/> on the first failure.
    /// </summary>
    /// <param name="input">The ISO 8601 value as submitted; surrounding whitespace is ignored.</param>
    /// <param name="nowUtc">The current time; the expiry must be strictly later.</param>
    public static ExpiryTime Parse(string? input, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The current time must be UTC.", nameof(nowUtc));
        }

        var trimmed = input?.Trim() ?? string.Empty;

        // 1. ISO 8601 with an explicit zone. A well-formed value missing only the zone gets its own
        // message, because that is the likely mistake. Parsing also rejects impossible dates and
        // offsets beyond ±14:00.
        if (!WithZone().IsMatch(trimmed)
            || !DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw Invalid(WithoutZone().IsMatch(trimmed)
                ? "The expiry needs a time zone, e.g. 2026-12-31T23:59:00Z or 2026-12-31T23:59:00+05:30."
                : FormatMessage);
        }

        // 2. Strictly in the future.
        var utc = parsed.UtcDateTime;
        if (utc <= nowUtc)
        {
            throw Invalid("The expiry must be in the future.");
        }

        return new ExpiryTime(utc);
    }

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]{1,7})?)?(Z|[+-][0-9]{2}:[0-9]{2})$")]
    private static partial Regex WithZone();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]{1,7})?)?$")]
    private static partial Regex WithoutZone();

    private static LinkValidationException Invalid(string message) => new(Field, message);
}