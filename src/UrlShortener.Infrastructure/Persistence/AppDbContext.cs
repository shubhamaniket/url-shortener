using Microsoft.EntityFrameworkCore;

using UrlShortener.Domain.Links;

namespace UrlShortener.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShortLink> ShortLinks => Set<ShortLink>();

    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}