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

    public async Task<bool> TryReplaceExpiredAsync(
        ShortLink expired, ShortLink replacement, DateTime nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expired);
        ArgumentNullException.ThrowIfNull(replacement);

        // Microsoft.Data.Sqlite starts an IMMEDIATE transaction: concurrent claims queue for the write
        // lock, and only the first finds the expired row still there to delete.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Conditional on still being expired; ON DELETE CASCADE removes its click events (data minimisation).
        var deleted = await db.ShortLinks
            .Where(link => link.Id == expired.Id && link.ExpiresAtUtc != null && link.ExpiresAtUtc <= nowUtc)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return false;
        }

        if (!await TryAddAsync(replacement, cancellationToken))
        {
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}