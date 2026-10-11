using Microsoft.EntityFrameworkCore;

using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Links;

/// <summary>
/// Writes a batch of click events and the matching <c>ClickCount</c> increases in one transaction, so
/// the stored total and the events always agree (research R5, Clarification Q1).
/// </summary>
public sealed class ClickEventStore(AppDbContext db) : IClickEventStore
{
    public async Task<int> SaveAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clicks);

        var linkIds = clicks.Select(click => click.ShortLinkId).Distinct().ToList();

        // IMMEDIATE transaction (Microsoft.Data.Sqlite default): a reclaim cannot delete a link
        // between the existence check and the insert.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // A link retired by a reclaim may still have clicks queued; skipping them keeps a foreign-key
        // failure from discarding the whole batch, including other links' clicks.
        var existing = (await db.ShortLinks
                .Where(link => linkIds.Contains(link.Id))
                .Select(link => link.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var kept = clicks.Where(click => existing.Contains(click.ShortLinkId)).ToList();

        db.ClickEvents.AddRange(kept);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var perLink in kept.GroupBy(click => click.ShortLinkId))
        {
            long added = perLink.Count();
            await db.ShortLinks
                .Where(link => link.Id == perLink.Key)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(link => link.ClickCount, link => link.ClickCount + added),
                    cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return clicks.Count - kept.Count;
    }
}