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
    IClickStatsReader statsReader,
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

        // One attempt: the unique index rejects an alias taken in any letter case (FR-012), unless the
        // link holding it has expired, in which case the code is reclaimed (FR-006a, research R6).
        if (!await repository.TryAddAsync(link, cancellationToken))
        {
            var current = await repository.FindByNormalizedCodeAsync(link.NormalizedCode, cancellationToken);
            if (current is null
                || !current.IsExpiredAt(createdAtUtc)
                || !await repository.TryReplaceExpiredAsync(current, link, createdAtUtc, cancellationToken))
            {
                throw new AliasAlreadyExistsException(alias.Value);
            }

            LogCodeReclaimed(logger, link.Code);
        }

        LogLinkCreated(logger, link.Code);
        return ToDetails(link);
    }

    public async Task<RedirectResolution> ResolveForRedirectAsync(
        string code, ClickContext click, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(click);

        var link = await FindAsync(code, cancellationToken);
        if (link is null)
        {
            return RedirectResolution.NotFound;
        }

        // An expired link stops redirecting and its visits are not clicks (FR-004).
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (link.IsExpiredAt(nowUtc))
        {
            return RedirectResolution.Expired;
        }

        // Off the redirect path (constitution V): the click is only queued; a background writer
        // stores it. ClickEvent.Create keeps just the referrer host and browser family.
        try
        {
            clickRecorder.TryRecord(ClickEvent.Create(link.Id, nowUtc, click.Referrer, click.UserAgent));
        }
        catch (Exception ex)
        {
            // Any recording failure is isolated from the redirect (FR-015a).
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

    /// <remarks>Ignores expiry (US3 scenario 5) and records no click (FR-022); "today" is the current UTC date.</remarks>
    public async Task<LinkStatistics?> GetStatsAsync(string code, CancellationToken cancellationToken)
    {
        var link = await FindAsync(code, cancellationToken);
        if (link is null)
        {
            return null;
        }

        var todayUtc = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return await statsReader.GetAsync(link, todayUtc, cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Reclaimed expired code {Code}; the expired link and its click history were deleted")]
    private static partial void LogCodeReclaimed(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generated short code collided on attempt {Attempt}")]
    private static partial void LogCodeCollision(ILogger logger, int attempt);
}