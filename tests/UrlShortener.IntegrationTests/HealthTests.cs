using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace UrlShortener.IntegrationTests;

public class HealthTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task HealthEndpointReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}