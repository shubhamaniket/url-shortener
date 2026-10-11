namespace UrlShortener.Application.Links;

/// <summary>A request to create a short link, with the values exactly as submitted (validated by the Domain).</summary>
/// <param name="Url">The destination URL as submitted.</param>
/// <param name="CustomAlias">The requested alias, or <c>null</c> for a generated code.</param>
/// <param name="ExpiresAt">The expiry as submitted (ISO 8601 with a zone), or <c>null</c> for a link that never expires.</param>
public sealed record CreateLinkCommand(string Url, string? CustomAlias, string? ExpiresAt = null);