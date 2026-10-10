using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

public sealed class CreateLinkTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri LinksUri = new("/api/links", UriKind.Relative);

    [Fact]
    public async Task CreatesLinkWithGeneratedCode()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, new { url = "https://example.com/some/long/path?x=1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await ReadJsonAsync(response);
        var code = body.GetProperty("code").GetString()!;
        Assert.True(GeneratedCode.IsValid(code), $"'{code}' is not a 7-character Base62 code.");
        Assert.Equal($"{ApiFactory.PublicBaseUrl}/{code}", body.GetProperty("shortUrl").GetString());
        Assert.Equal("https://example.com/some/long/path?x=1", body.GetProperty("originalUrl").GetString());
        Assert.EndsWith("Z", body.GetProperty("createdAtUtc").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, body.GetProperty("clickCount").GetInt64());
        Assert.Equal($"/api/links/{code}", response.Headers.Location?.OriginalString);

        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks
            .SingleAsync(link => link.Code == code);
        Assert.Equal("https://example.com/some/long/path?x=1", stored.OriginalUrl);
    }

    [Fact]
    public async Task SameUrlTwiceGetsTwoDifferentCodes()
    {
        var client = factory.CreateClient();

        var first = await ReadJsonAsync(await client.PostAsJsonAsync(LinksUri, new { url = "https://example.com/same" }));
        var second = await ReadJsonAsync(await client.PostAsJsonAsync(LinksUri, new { url = "https://example.com/same" }));

        Assert.NotEqual(first.GetProperty("code").GetString(), second.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://files.example.com/a")]
    [InlineData("localhost:8080")]
    [InlineData("/some/path")]
    [InlineData("https://google.com@evil.example/login")]
    [InlineData("http://LOCALHOST:5058/x")]
    [InlineData("   ")]
    public async Task InvalidUrlReturns400AndStoresNothing(string url)
    {
        var before = await CountLinksAsync();

        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, new { url });

        await AssertUrlErrorAsync(response);
        Assert.Equal(before, await CountLinksAsync());
    }

    [Fact]
    public async Task MissingSchemeReturnsHint()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, new { url = "www.example.com/page" });

        var message = await AssertUrlErrorAsync(response);
        Assert.Equal("The URL must start with http:// or https://. Did you mean https://www.example.com/page?", message);
    }

    [Fact]
    public async Task MissingUrlFieldReturns400WithCamelCaseKey()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, new { });

        var message = await AssertUrlErrorAsync(response);
        Assert.Equal("A URL is required.", message);
    }

    [Fact]
    public async Task EmptyBodyReturns400ProblemDetails()
    {
        using var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        var response = await factory.CreateClient().PostAsync(LinksUri, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await ReadJsonAsync(response)).TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ForgedHostHeaderDoesNotChangeShortUrl()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, LinksUri)
        {
            Content = JsonContent.Create(new { url = "https://example.com/forged" }),
        };
        request.Headers.Host = "evil.example";

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var shortUrl = (await ReadJsonAsync(response)).GetProperty("shortUrl").GetString();
        Assert.StartsWith($"{ApiFactory.PublicBaseUrl}/", shortUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExhaustedCodeGenerationReturnsGeneric500()
    {
        var client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<IShortCodeGenerator>(new ConstantCodeGenerator("Qq0Qq0Q"))))
            .CreateClient();

        var first = await client.PostAsJsonAsync(LinksUri, new { url = "https://example.com/first" });
        var second = await client.PostAsJsonAsync(LinksUri, new { url = "https://example.com/second" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var raw = await second.Content.ReadAsStringAsync();
        Assert.Equal("An unexpected error occurred.", JsonDocument.Parse(raw).RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain("CodeGenerationFailedException", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", raw, StringComparison.Ordinal);
    }

    private static async Task<string?> AssertUrlErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.False(errors.TryGetProperty("Url", out _), "Error keys must be camelCase.");
        return errors.GetProperty("url")[0].GetString();
    }

    private async Task<int> CountLinksAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks.CountAsync();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private sealed class ConstantCodeGenerator(string code) : IShortCodeGenerator
    {
        public string Generate() => code;
    }
}