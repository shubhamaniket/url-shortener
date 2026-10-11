using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.Contracts;

/// <summary>Body of <c>POST /api/links</c>. Shape checks only; link rules live in the Domain.</summary>
public sealed class CreateLinkRequest
{
    /// <summary>The absolute http or https URL to shorten.</summary>
    /// <example>https://example.com/some/long/path?x=1</example>
    [Required(ErrorMessage = "A URL is required.")]
    public string? Url { get; init; }

    /// <summary>Optional custom alias: 3–30 letters, digits, '-' or '_'.</summary>
    /// <example>team-offsite</example>
    public string? CustomAlias { get; init; }

    /// <summary>
    /// Optional expiry: ISO 8601 with a time zone (<c>Z</c> or an offset), in the future. Kept as text
    /// so a value without a zone is rejected rather than read as server-local time.
    /// </summary>
    /// <example>2099-12-31T23:59:00+05:30</example>
    public string? ExpiresAt { get; init; }
}