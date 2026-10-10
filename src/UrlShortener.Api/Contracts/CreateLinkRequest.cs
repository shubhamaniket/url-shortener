using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.Contracts;

/// <summary>Body of <c>POST /api/links</c>. Shape checks only; link rules live in the Domain.</summary>
public sealed class CreateLinkRequest
{
    /// <example>https://example.com/some/long/path?x=1</example>
    [Required(ErrorMessage = "A URL is required.")]
    public string? Url { get; init; }

    /// <summary>Optional custom alias: 3–30 letters, digits, '-' or '_'.</summary>
    /// <example>team-offsite</example>
    public string? CustomAlias { get; init; }
}