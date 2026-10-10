using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace UrlShortener.IntegrationTests;

public class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task HealthyWhenDatabaseIsReachable()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnhealthyWhenDatabaseIsUnreachable()
    {
        // A database file inside a directory that does not exist cannot be opened or created.
        var unreachable = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "urlshortener.db");
        using var unhealthyFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={unreachable}",
                    ["Database:MigrateOnStartup"] = "false",
                })));

        var response = await unhealthyFactory.CreateClient().GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}