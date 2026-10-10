using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

public sealed class CustomAliasTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri LinksUri = new("/api/links", UriKind.Relative);

    [Fact]
    public async Task CreatesLinkWithAliasKeepingCase()
    {
        var alias = NewAlias("Team");

        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, new { url = "https://example.com/offsite", customAlias = alias });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(alias, body.GetProperty("code").GetString());
        Assert.Equal($"{ApiFactory.PublicBaseUrl}/{alias}", body.GetProperty("shortUrl").GetString());
        Assert.Equal($"/api/links/{alias}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AliasTakenInAnotherCaseReturns409AndKeepsFirstLink()
    {
        var alias = NewAlias("team");
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync(LinksUri, new { url = "https://example.com/first", customAlias = alias })).StatusCode);

        var upper = alias.ToUpperInvariant();
        var response = await client.PostAsJsonAsync(LinksUri, new { url = "https://example.com/second", customAlias = upper });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Alias already in use", body.GetProperty("title").GetString());
        Assert.Equal($"The alias '{upper}' is already taken.", body.GetProperty("detail").GetString());
        Assert.Equal("https://example.com/first", await StoredUrlAsync(alias));
    }

    [Fact]
    public async Task AliasMatchingExistingGeneratedCodeIgnoringCaseReturns409()
    {
        var client = factory.CreateClient();
        var created = await ReadJsonAsync(await client.PostAsJsonAsync(LinksUri, new { url = "https://example.com/generated" }));
        var code = created.GetProperty("code").GetString()!;
        var alias = code.Any(char.IsUpper) ? code.ToLowerInvariant() : code.ToUpperInvariant();

        var response = await client.PostAsJsonAsync(LinksUri, new { url = "https://example.com/alias", customAlias = alias });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("API")]
    [InlineData("health")]
    [InlineData("ab")]
    [InlineData("my alias")]
    [InlineData("a.b")]
    [InlineData("")]
    public async Task InvalidAliasReturns400AndStoresNothing(string alias)
    {
        var before = await CountLinksAsync();

        var response = await factory.CreateClient().PostAsJsonAsync(LinksUri, new { url = "https://example.com/a", customAlias = alias });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.False(string.IsNullOrEmpty(errors.GetProperty("customAlias")[0].GetString()));
        Assert.Equal(before, await CountLinksAsync());
    }

    [Fact]
    public async Task AliasRedirectsInAnyCase()
    {
        var alias = NewAlias("Offsite");
        await factory.CreateClient().PostAsJsonAsync(LinksUri, new { url = "https://example.com/offsite", customAlias = alias });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        foreach (var variant in new[] { alias, alias.ToUpperInvariant(), alias.ToLowerInvariant() })
        {
            var response = await client.GetAsync(new Uri($"/{variant}", UriKind.Relative));

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal("https://example.com/offsite", response.Headers.Location?.OriginalString);
        }
    }

    /// <summary>A unique, valid alias per test (the class shares one database).</summary>
    private static string NewAlias(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<string> StoredUrlAsync(string alias)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var normalized = alias.ToLowerInvariant();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks
            .Where(link => link.NormalizedCode == normalized)
            .Select(link => link.OriginalUrl)
            .SingleAsync();
    }

    private async Task<int> CountLinksAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks.CountAsync();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}