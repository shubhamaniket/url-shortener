using Microsoft.EntityFrameworkCore;

using UrlShortener.Application.Abstractions;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Links;

/// <summary>
/// Increments the click count with a single atomic <c>UPDATE ... SET ClickCount = ClickCount + 1</c>,
/// so concurrent redirects never lose increments (research R6). Temporary: feature 002 replaces it
/// with a background recorder.
/// </summary>
internal sealed class DbClickRecorder(AppDbContext db) : IClickRecorder
{
    public async Task RecordClickAsync(long linkId, CancellationToken cancellationToken) =>
        await db.ShortLinks
            .Where(link => link.Id == linkId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(link => link.ClickCount, link => link.ClickCount + 1),
                cancellationToken);
}