using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

public sealed class StartupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task StartupAppliesMigrations()
    {
        _ = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Guards against the host ignoring the factory's connection string and silently
        // sharing a default database file between test classes.
        Assert.Equal(factory.ConnectionString, db.Database.GetConnectionString());
        Assert.True(File.Exists(factory.DatabasePath));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.ShortLinks.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://files.example")]
    [InlineData("localhost:5058")]
    public void StartupFailsFastOnInvalidPublicBaseUrl(string value)
    {
        using var invalidFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ShortLinks:PublicBaseUrl"] = value })));

        var exception = Record.Exception(() => invalidFactory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("ShortLinks:PublicBaseUrl", Flatten(exception), StringComparison.Ordinal);
    }

    private static string Flatten(Exception exception) =>
        exception is AggregateException aggregate
            ? string.Join(" | ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
            : exception.ToString();
}