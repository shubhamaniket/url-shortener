namespace UrlShortener.Application.Abstractions;

/// <summary>
/// Records a click for a link. Callers on the redirect path must isolate failures so a redirect
/// never fails because recording failed (FR-015a).
/// </summary>
public interface IClickRecorder
{
    Task RecordClickAsync(long linkId, CancellationToken cancellationToken);
}