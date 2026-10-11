using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests.Persistence;

/// <summary>Runs the repository against a real, migrated SQLite file (one per test).</summary>
public sealed class ShortLinkRepositoryTests : IAsyncLifetime
{
    private static readonly DateTime CreatedAt = new(2026, 10, 10, 13, 45, 0, DateTimeKind.Utc);

    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"urlshortener-test-{Guid.NewGuid():N}.db");
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_databasePath}",
            })
            .Build();

        _services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddInfrastructure()
            .BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    [Fact]
    public async Task AddedLinkCanBeFoundByNormalizedCode()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var link = ShortLink.CreateWithCustomAlias("Team-Offsite", "https://example.com/a", CreatedAt);
            Assert.True(await Repository(scope).TryAddAsync(link, CancellationToken.None));
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            var found = await Repository(scope).FindByNormalizedCodeAsync("team-offsite", CancellationToken.None);

            Assert.NotNull(found);
            Assert.True(found.Id > 0);
            Assert.Equal("Team-Offsite", found.Code);
            Assert.Equal("team-offsite", found.NormalizedCode);
            Assert.True(found.IsCustomAlias);
            Assert.Equal("https://example.com/a", found.OriginalUrl);
            Assert.Equal(CreatedAt, found.CreatedAtUtc);
            Assert.Equal(DateTimeKind.Utc, found.CreatedAtUtc.Kind);
            Assert.Equal(0, found.ClickCount);
        }
    }

    [Fact]
    public async Task FindReturnsNullForUnknownCode()
    {
        await using var scope = _services.CreateAsyncScope();

        Assert.Null(await Repository(scope).FindByNormalizedCodeAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task AddWithSameNormalizedCodeInDifferentCaseReturnsFalse()
    {
        await using var scope = _services.CreateAsyncScope();
        var repository = Repository(scope);

        var first = ShortLink.CreateWithCustomAlias("team-offsite", "https://example.com/a", CreatedAt);
        var second = ShortLink.CreateWithCustomAlias("Team-Offsite", "https://example.com/b", CreatedAt);

        Assert.True(await repository.TryAddAsync(first, CancellationToken.None));
        Assert.False(await repository.TryAddAsync(second, CancellationToken.None));
        Assert.Equal(1, await Db(scope).ShortLinks.CountAsync());
    }

    [Fact]
    public async Task SameContextCanAddAgainAfterADuplicate()
    {
        // The create use case retries generated codes on the same request-scoped context, so a
        // rejected insert must not stay tracked and be re-sent with the next save.
        await using var scope = _services.CreateAsyncScope();
        var repository = Repository(scope);

        Assert.True(await repository.TryAddAsync(
            ShortLink.CreateWithGeneratedCode("Abc123x", "https://example.com/a", CreatedAt), CancellationToken.None));
        Assert.False(await repository.TryAddAsync(
            ShortLink.CreateWithGeneratedCode("ABC123X", "https://example.com/b", CreatedAt), CancellationToken.None));
        Assert.True(await repository.TryAddAsync(
            ShortLink.CreateWithGeneratedCode("Xyz789q", "https://example.com/c", CreatedAt), CancellationToken.None));

        Assert.Equal(2, await Db(scope).ShortLinks.CountAsync());
    }

    [Fact]
    public async Task MigrationCreatesUniqueIndexOnNormalizedCode()
    {
        await using var scope = _services.CreateAsyncScope();
        var connection = Db(scope).Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_ShortLinks_NormalizedCode'";

        var sql = (string?)await command.ExecuteScalarAsync();

        Assert.NotNull(sql);
        Assert.Contains("UNIQUE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"NormalizedCode\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceExpiredDoesNothingWhileTheLinkHasNotExpired()
    {
        var expiresAt = CreatedAt.AddHours(1);
        await using var scope = _services.CreateAsyncScope();
        var repository = Repository(scope);
        await repository.TryAddAsync(
            ShortLink.CreateWithCustomAlias("promo", "https://example.com/old", CreatedAt, expiresAt), CancellationToken.None);
        var current = (await repository.FindByNormalizedCodeAsync("promo", CancellationToken.None))!;

        var replaced = await repository.TryReplaceExpiredAsync(
            current,
            ShortLink.CreateWithCustomAlias("Promo", "https://example.com/new", expiresAt.AddTicks(-1)),
            expiresAt.AddTicks(-1),
            CancellationToken.None);

        Assert.False(replaced);
        Assert.Equal("https://example.com/old", (await Db(scope).ShortLinks.AsNoTracking().SingleAsync()).OriginalUrl);
    }

    [Fact]
    public async Task ReplaceExpiredRollsBackTheDeleteWhenTheInsertFails()
    {
        var expiresAt = CreatedAt.AddHours(1);
        await using var scope = _services.CreateAsyncScope();
        var repository = Repository(scope);
        await repository.TryAddAsync(
            ShortLink.CreateWithCustomAlias("promo", "https://example.com/old", CreatedAt, expiresAt), CancellationToken.None);
        await repository.TryAddAsync(
            ShortLink.CreateWithCustomAlias("taken", "https://example.com/other", CreatedAt), CancellationToken.None);
        var expired = (await repository.FindByNormalizedCodeAsync("promo", CancellationToken.None))!;

        // The replacement's code is held by a live link, so the insert hits the unique index.
        var replaced = await repository.TryReplaceExpiredAsync(
            expired, ShortLink.CreateWithCustomAlias("taken", "https://example.com/new", expiresAt), expiresAt, CancellationToken.None);

        Assert.False(replaced);
        Assert.Equal(
            ["https://example.com/old", "https://example.com/other"],
            await Db(scope).ShortLinks.AsNoTracking().OrderBy(link => link.Id).Select(link => link.OriginalUrl).ToListAsync());
    }

    private static IShortLinkRepository Repository(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IShortLinkRepository>();

    private static AppDbContext Db(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AppDbContext>();
}