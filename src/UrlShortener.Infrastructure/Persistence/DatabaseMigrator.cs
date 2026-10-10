using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace UrlShortener.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>Applies pending EF Core migrations; the database file is created if missing.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken);
    }
}