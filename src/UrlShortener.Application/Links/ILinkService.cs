namespace UrlShortener.Application.Links;

public interface ILinkService
{
    /// <summary>
    /// Creates a short link, optionally expiring. Throws <see cref="Domain.Links.LinkValidationException"/>
    /// for invalid input and <see cref="CodeGenerationFailedException"/> when no unique code could be found.
    /// </summary>
    Task<LinkDetails> CreateAsync(CreateLinkCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a code to its original URL and records the click best-effort: a recording failure
    /// never changes the result (FR-015a). Unknown codes give <see cref="RedirectResolution.NotFound"/>;
    /// expired links give <see cref="RedirectResolution.Expired"/> and record no click (FR-004).
    /// </summary>
    Task<RedirectResolution> ResolveForRedirectAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a link's details and click count, or <c>null</c> when no link matches. Reading
    /// details never records a click (FR-018) and works for expired links (FR-005).
    /// </summary>
    Task<LinkDetails?> GetDetailsAsync(string code, CancellationToken cancellationToken);
}