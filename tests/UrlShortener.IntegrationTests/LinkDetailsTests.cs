using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace UrlShortener.IntegrationTests;

public sealed class LinkDetailsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri LinksUri = new("/api/links", UriKind.Relative);

    [Fact]
    public async Task NewLinkHasExactlyTheContractFieldsAndZeroClicks()
    {
        var code = await CreateLinkAsync(new { url = "https://example.com/details" });

        var response = await factory.CreateClient().GetAsync(DetailsUri(code));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.Equal(
            ["clickCount", "code", "createdAtUtc", "originalUrl", "shortUrl"],
            body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal($"{ApiFactory.PublicBaseUrl}/{code}", body.GetProperty("shortUrl").GetString());
        Assert.Equal("https://example.com/details", body.GetProperty("originalUrl").GetString());
        Assert.EndsWith("Z", body.GetProperty("createdAtUtc").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, body.GetProperty("clickCount").GetInt64());
    }

    [Fact]
    public async Task ShowsClickCountAndReadingDetailsDoesNotCount()
    {
        var code = await CreateLinkAsync(new { url = "https://example.com/clicked" });
        var redirectClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        for (var i = 0; i < 3; i++)
        {
            await redirectClient.GetAsync(new Uri($"/{code}", UriKind.Relative));
        }

        var client = factory.CreateClient();
        var first = await ReadJsonAsync(await client.GetAsync(DetailsUri(code)));
        var second = await ReadJsonAsync(await client.GetAsync(DetailsUri(code)));

        Assert.Equal(3, first.GetProperty("clickCount").GetInt64());
        Assert.Equal(3, second.GetProperty("clickCount").GetInt64());
    }

    [Fact]
    public async Task AliasIsFoundInAnyCaseAndKeepsItsOriginalCase()
    {
        var alias = $"Details-{Guid.NewGuid():N}"[..20];
        await CreateLinkAsync(new { url = "https://example.com/alias", customAlias = alias });

        var response = await factory.CreateClient().GetAsync(DetailsUri(alias.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(alias, (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownCodeReturnsProblemDetails404()
    {
        var response = await factory.CreateClient().GetAsync(DetailsUri("zzzzzzz"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task LocationFromCreateResolvesToTheDetails()
    {
        var created = await factory.CreateClient().PostAsJsonAsync(LinksUri, new { url = "https://example.com/location" });
        var code = (await ReadJsonAsync(created)).GetProperty("code").GetString();

        var response = await factory.CreateClient().GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(code, (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    private async Task<string> CreateLinkAsync(object request)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, request);
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("code").GetString()!;
    }

    private static Uri DetailsUri(string code) => new($"/api/links/{code}", UriKind.Relative);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}