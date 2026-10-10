using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Options;
using UrlShortener.Domain.Links;

namespace UrlShortener.Application.Links;

public sealed partial class LinkService(
    IShortLinkRepository repository,
    IShortCodeGenerator codeGenerator,
    IOptions<ShortLinkOptions> options,
    TimeProvider timeProvider,
    ILogger<LinkService> logger) : ILinkService
{
    /// <summary>Total attempts to find an unused generated code (FR-007).</summary>
    public const int MaxGenerationAttempts = 5;

    private readonly ShortLinkOptions _settings = options.Value;

    public async Task<LinkDetails> CreateAsync(CreateLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var destination = DestinationUrl.Parse(command.Url, _settings.BaseUri.Host);
        var createdAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (command.CustomAlias is not null)
        {
            // Replaced by the alias path in T017.
            throw new LinkValidationException("customAlias", "Custom aliases are not supported yet.");
        }

        // The unique index decides; a collision (including with an alias in another letter case)
        // just means trying a new code.
        for (var attempt = 1; attempt <= MaxGenerationAttempts; attempt++)
        {
            var link = ShortLink.CreateWithGeneratedCode(codeGenerator.Generate(), destination.Value, createdAtUtc);

            if (await repository.TryAddAsync(link, cancellationToken))
            {
                LogLinkCreated(logger, link.Code);
                return ToDetails(link);
            }

            LogCodeCollision(logger, attempt);
        }

        throw new CodeGenerationFailedException(MaxGenerationAttempts);
    }

    private LinkDetails ToDetails(ShortLink link) => new(
        link.Code,
        _settings.BuildShortUrl(link.Code),
        link.OriginalUrl,
        link.CreatedAtUtc,
        link.ClickCount);

    // Log codes only: destination URLs can carry personal data in their query strings.
    [LoggerMessage(Level = LogLevel.Information, Message = "Created short link {Code}")]
    private static partial void LogLinkCreated(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generated short code collided on attempt {Attempt}")]
    private static partial void LogCodeCollision(ILogger logger, int attempt);
}