using UrlShortener.Domain.Links;

namespace UrlShortener.Application.Abstractions;

/// <summary>
/// Hands a click to background storage without waiting (research R4): the redirect path does no
/// analytics I/O. Delivery is at-most-once (ADR-005). Callers still isolate failures so a redirect
/// never fails because recording failed (FR-015a).
/// </summary>
public interface IClickRecorder
{
    /// <summary>Queues the click; returns <c>false</c> if it could not be queued. Never blocks.</summary>
    bool TryRecord(ClickEvent clickEvent);
}