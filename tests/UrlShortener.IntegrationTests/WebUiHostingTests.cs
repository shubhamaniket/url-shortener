using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// The API serves the built web UI (feature 004): the page at "/", its files under /assets, and short
/// codes and the API exactly as before. Uses its own web root so it does not depend on a local build.
/// </summary>
public sealed class WebUiHostingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IDisposable
{
    private readonly string _webRoot = CreateWebRoot();

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Fact]
    public async Task RootServesThePage()
    {
        using var host = Host();

        var response = await host.CreateClient().GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<div id=\"root\"></div>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssetsAreServedAsFilesNotTreatedAsShortCodes()
    {
        using var host = Host();

        var response = await host.CreateClient().GetAsync(new Uri("/assets/index-test.js", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ShortCodesStillRedirectAndUnknownPathsAreNotThePage()
    {
        using var host = Host();
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var created = await client.PostAsync(
            new Uri("/api/links", UriKind.Relative),
            new StringContent("""{"url":"https://example.com/ui"}""", System.Text.Encoding.UTF8, "application/json"));
        var code = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

        var redirect = await client.GetAsync(new Uri($"/{code}", UriKind.Relative));
        var unknown = await client.GetAsync(new Uri("/zzzzzzz", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
    }

    private WebApplicationFactory<Program> Host() =>
        factory.WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot));

    private static string CreateWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"urlshortener-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><body><div id=\"root\"></div></body></html>");
        File.WriteAllText(Path.Combine(root, "assets", "index-test.js"), "console.log('ui');");
        return root;
    }
}