using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests.Persistence;

/// <summary>
/// Migration <c>AddExpiryAndClickEvents</c> (feature 002) on a database that already holds feature
/// 001 data: existing links and counts must survive, and the schema must round-trip (research R9).
/// </summary>
public sealed class MigrationTests : IAsyncLifetime
{
    private const string InitialCreate = "20261010190734_InitialCreate";

    private static readonly DateTime CreatedAt = new(2026, 10, 10, 13, 45, 0, DateTimeKind.Utc);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"urlshortener-migration-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<AppDbContext> _options;

    public MigrationTests()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;
    }

    public async Task InitializeAsync()
    {
        // A version 1 database, as feature 001 left it, holding a link that has been clicked 7 times.
        await using var db = new AppDbContext(_options);
        await Migrator(db).MigrateAsync(InitialCreate);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ShortLinks (Code, NormalizedCode, IsCustomAlias, OriginalUrl, CreatedAtUtc, ClickCount) " +
            "VALUES ('Team-Offsite', 'team-offsite', 1, 'https://example.com/a', {0}, 7)",
            CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    private static IMigrator Migrator(AppDbContext db) => db.GetService<IMigrator>();

    [Fact]
    public async Task ExistingLinkKeepsItsDataAndNeverExpires()
    {
        await using var db = new AppDbContext(_options);
        await Migrator(db).MigrateAsync();

        var link = await db.ShortLinks.AsNoTracking().SingleAsync();

        Assert.Equal("Team-Offsite", link.Code);
        Assert.True(link.IsCustomAlias);
        Assert.Equal("https://example.com/a", link.OriginalUrl);
        Assert.Equal(CreatedAt, link.CreatedAtUtc);
        Assert.Equal(7, link.ClickCount);
        Assert.Null(link.ExpiresAtUtc);
        Assert.False(link.IsExpiredAt(DateTime.MaxValue));
    }

    [Fact]
    public async Task ExpiryAndClickEventsAreStoredAndReadBackAsUtc()
    {
        await using var db = new AppDbContext(_options);
        await Migrator(db).MigrateAsync();
        var expiresAt = CreatedAt.AddDays(1);
        var link = ShortLink.CreateWithGeneratedCode("Abc123x", "https://example.com/b", CreatedAt, expiresAt);
        db.ShortLinks.Add(link);
        await db.SaveChangesAsync();
        db.ClickEvents.Add(ClickEvent.Create(link.Id, CreatedAt.AddHours(1), "https://news.example/a?u=1", null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.ShortLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id);
        var click = await db.ClickEvents.AsNoTracking().SingleAsync();

        Assert.Equal(expiresAt, stored.ExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, stored.ExpiresAtUtc!.Value.Kind);
        Assert.Equal(link.Id, click.ShortLinkId);
        Assert.Equal(CreatedAt.AddHours(1), click.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, click.OccurredAtUtc.Kind);
        Assert.Equal("news.example", click.ReferrerHost);
        Assert.Equal(UserAgentFamily.Other, click.UserAgentFamily);
    }

    [Fact]
    public async Task DeletingALinkDeletesItsClickEvents()
    {
        await using var db = new AppDbContext(_options);
        await Migrator(db).MigrateAsync();
        var linkId = await db.ShortLinks.Select(l => l.Id).SingleAsync();
        db.ClickEvents.AddRange(
            ClickEvent.Create(linkId, CreatedAt.AddHours(1), null, null),
            ClickEvent.Create(linkId, CreatedAt.AddHours(2), null, null));
        await db.SaveChangesAsync();

        // Deleted in SQL, not through the change tracker, so the database's ON DELETE CASCADE is what runs.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ShortLinks WHERE Id = {0}", linkId);

        Assert.Equal(0, await db.ClickEvents.CountAsync());
    }

    [Fact]
    public async Task MigrationCreatesTheClickEventIndex()
    {
        await using var db = new AppDbContext(_options);
        await Migrator(db).MigrateAsync();

        var sql = await ScalarAsync(db,
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_ClickEvents_ShortLinkId_OccurredAtUtc'");

        Assert.NotNull(sql);
        Assert.Contains("\"ShortLinkId\", \"OccurredAtUtc\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownThenUpKeepsExistingLinks()
    {
        await using var db = new AppDbContext(_options);
        await Migrator(db).MigrateAsync();

        await Migrator(db).MigrateAsync(InitialCreate);

        Assert.Null(await ScalarAsync(db, "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'ClickEvents'"));
        Assert.DoesNotContain("ExpiresAtUtc", await ScalarAsync(db, "SELECT sql FROM sqlite_master WHERE name = 'ShortLinks'"), StringComparison.Ordinal);
        Assert.Equal("7", await ScalarAsync(db, "SELECT ClickCount FROM ShortLinks WHERE NormalizedCode = 'team-offsite'"));

        await Migrator(db).MigrateAsync();

        var link = await db.ShortLinks.AsNoTracking().SingleAsync();
        Assert.Equal(7, link.ClickCount);
        Assert.Null(link.ExpiresAtUtc);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    private static async Task<string?> ScalarAsync(AppDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString();
    }
}