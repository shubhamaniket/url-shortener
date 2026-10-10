using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UrlShortener.Infrastructure.Persistence;

/// <summary>Used only by the <c>dotnet ef</c> tools to create migrations.</summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(DependencyInjection.DefaultConnectionString)
            .Options);
}