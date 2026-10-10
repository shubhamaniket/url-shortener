using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Links;

namespace UrlShortener.Infrastructure.Persistence;

internal sealed class ShortLinkRepository(AppDbContext db) : IShortLinkRepository
{
    // SQLITE_CONSTRAINT_UNIQUE: a unique index rejected the insert.
    private const int SqliteConstraintUnique = 2067;

    public Task<ShortLink?> FindByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken) =>
        db.ShortLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(link => link.NormalizedCode == normalizedCode, cancellationToken);

    public async Task<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken)
    {
        db.ShortLinks.Add(link);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique })
        {
            // Stop tracking the rejected entity so a retry on the same context does not resend it.
            db.Entry(link).State = EntityState.Detached;
            return false;
        }
    }
}