using UrlShortener.Domain.Links;

namespace UrlShortener.Infrastructure.Links;

/// <summary>The click writer's data access, separate so tests can make storage fail or stall (research R11).</summary>
public interface IClickEventStore
{
    /// <summary>
    /// Stores the events and increases each link's stored total by its number of events, all in one
    /// transaction. Events whose link no longer exists are skipped. Returns how many were skipped.
    /// </summary>
    Task<int> SaveAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken);
}