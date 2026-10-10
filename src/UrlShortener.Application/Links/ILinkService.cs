namespace UrlShortener.Application.Links;

public interface ILinkService
{
    /// <summary>
    /// Creates a short link. Throws <see cref="Domain.Links.LinkValidationException"/> for invalid
    /// input and <see cref="CodeGenerationFailedException"/> when no unique code could be found.
    /// </summary>
    Task<LinkDetails> CreateAsync(CreateLinkCommand command, CancellationToken cancellationToken);
}