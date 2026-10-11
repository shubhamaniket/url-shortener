using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Options;
using UrlShortener.Domain.Links;

namespace UrlShortener.Application.Links;

public sealed partial class LinkService(
    IShortLinkRepository repository,
    IShortCodeGenerator codeGenerator,
    IClickRecorder clickRecorder,
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

        // Fields are validated in request order (url, customAlias, expiresAt); the first failure is reported.
        var destination = DestinationUrl.Parse(command.Url, _settings.BaseUri.Host);
        var alias = command.CustomAlias is null ? null : CustomAlias.Parse(command.CustomAlias);
        var createdAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = command.ExpiresAt is null ? (DateTime?)null : ExpiryTime.Parse(command.ExpiresAt, createdAtUtc).Utc;

        if (alias is not null)
        {
            return await CreateWithAliasAsync(alias, destination, createdAtUtc, expiresAtUtc, cancellationToken);
        }

        // The unique index decides; a collision (including with an alias in another letter case)
        // just means trying a new code.
        for (var attempt = 1; attempt <= MaxGenerationAttempts; attempt++)
        {
            var link = ShortLink.CreateWithGeneratedCode(codeGenerator.Generate(), destination.Value, createdAtUtc, expiresAtUtc);

            if (await repository.TryAddAsync(link, cancellationToken))
            {
                LogLinkCreated(logger, link.Code);
                return ToDetails(link);
            }

            LogCodeCollision(logger, attempt);
        }

        throw new CodeGenerationFailedException(MaxGenerationAttempts);
    }

    private async Task<LinkDetails> CreateWithAliasAsync(
        CustomAlias alias,
        DestinationUrl destination,
        DateTime createdAtUtc,
        DateTime? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var link = ShortLink.CreateWithCustomAlias(alias.Value, destination.Value, createdAtUtc, expiresAtUtc);

        // One attempt: the unique index rejects an alias taken in any letter case (FR-012).
        if (!await repository.TryAddAsync(link, cancellationToken))
        {
            throw new AliasAlreadyExistsException(alias.Value);
        }

        LogLinkCreated(logger, link.Code);
        return ToDetails(link);
    }

    public async Task<RedirectResolution> ResolveForRedirectAsync(string code, CancellationToken cancellationToken)
    {
        var link = await FindAsync(code, cancellationToken);
        if (link is null)
        {
            return RedirectResolution.NotFound;
        }

        // An expired link stops redirecting and its visits are not clicks (FR-004).
        if (link.IsExpiredAt(timeProvider.GetUtcNow().UtcDateTime))
        {
            return RedirectResolution.Expired;
        }

        // Temporary synchronous write (plan Complexity Tracking, Principle V); feature 002 moves
        // click recording off the redirect path. Failures are isolated here so the redirect never
        // depends on analytics.
        try
        {
            await clickRecorder.RecordClickAsync(link.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Any other recording failure is isolated from the redirect (FR-015a).
            LogClickRecordingFailed(logger, ex, link.Code);
        }

        return RedirectResolution.Found(link.OriginalUrl);
    }

    /// <remarks>Ignores expiry: an expired link's details stay readable (FR-005).</remarks>
    public async Task<LinkDetails?> GetDetailsAsync(string code, CancellationToken cancellationToken)
    {
        var link = await FindAsync(code, cancellationToken);
        return link is null ? null : ToDetails(link);
    }

    /// <summary>One indexed lookup, then the case rules: aliases in any case, generated codes exactly.</summary>
    private async Task<ShortLink?> FindAsync(string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);

        var link = await repository.FindByNormalizedCodeAsync(ShortLink.Normalize(code), cancellationToken);
        return link is not null && link.Matches(code) ? link : null;
    }

    private LinkDetails ToDetails(ShortLink link) => new(
        link.Code,
        _settings.BuildShortUrl(link.Code),
        link.OriginalUrl,
        link.CreatedAtUtc,
        link.ExpiresAtUtc,
        link.ClickCount);

    // Log codes only: destination URLs can carry personal data in their query strings.
    [LoggerMessage(Level = LogLevel.Information, Message = "Created short link {Code}")]
    private static partial void LogLinkCreated(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording a click for {Code} failed; redirect continues")]
    private static partial void LogClickRecordingFailed(ILogger logger, Exception exception, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generated short code collided on attempt {Attempt}")]
    private static partial void LogCodeCollision(ILogger logger, int attempt);
}