namespace UrlShortener.Application.Links;

public interface ILinkService
{
    /// <summary>
    /// Creates a short link. Throws <see cref="Domain.Links.LinkValidationException"/> for invalid
    /// input and <see cref="CodeGenerationFailedException"/> when no unique code could be found.
    /// </summary>
    Task<LinkDetails> CreateAsync(CreateLinkCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the original URL for a code, or <c>null</c> when no link matches, and records the
    /// click best-effort: a recording failure never changes the result (FR-015a).
    /// </summary>
    Task<string?> ResolveForRedirectAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a link's details and click count, or <c>null</c> when no link matches. Reading
    /// details never records a click (FR-018).
    /// </summary>
    Task<LinkDetails?> GetDetailsAsync(string code, CancellationToken cancellationToken);
}