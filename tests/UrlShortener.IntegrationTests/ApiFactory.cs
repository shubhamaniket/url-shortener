using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Hosts the API against its own temporary SQLite file with migrations applied. Runs in the
/// <c>Testing</c> environment (not the factory default <c>Development</c>) so production-like
/// behaviour, such as error responses without stack traces, is what gets tested.
/// Tests replace services with <c>WithWebHostBuilder(b =&gt; b.ConfigureTestServices(...))</c>.
/// <see cref="Clock"/> follows real time until a test moves it forward (expiry tests).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string PublicBaseUrl = "http://localhost:5058";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"urlshortener-test-{Guid.NewGuid():N}.db");

    public AdjustableTimeProvider Clock { get; } = new();

    public string DatabasePath => _databasePath;

    public string ConnectionString => $"Data Source={_databasePath}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Database:MigrateOnStartup"] = "true",
                ["ShortLinks:PublicBaseUrl"] = PublicBaseUrl,
            }));
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }
}