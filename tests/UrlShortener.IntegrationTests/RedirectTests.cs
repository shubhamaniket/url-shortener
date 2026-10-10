using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Application.Abstractions;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests;

public sealed class RedirectTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task RedirectsWith302ToOriginalUrlAndNoStore()
    {
        var code = await CreateLinkAsync("https://example.com/target?x=1");

        var response = await factory.CreateClient(NoRedirects).GetAsync(Relative(code));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://example.com/target?x=1", response.Headers.Location?.OriginalString);
        Assert.True(response.Headers.CacheControl?.NoStore, "Cache-Control must be no-store so every visit is counted.");
    }

    [Fact]
    public async Task EachRedirectIsCounted()
    {
        var code = await CreateLinkAsync("https://example.com/counted");
        var client = factory.CreateClient(NoRedirects);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Found, (await client.GetAsync(Relative(code))).StatusCode);
        }

        Assert.Equal(3, await StoredClickCountAsync(code));
    }

    [Theory]
    [InlineData("zzzzzzz")]
    [InlineData("api")]
    [InlineData("a.b")]
    [InlineData("ab")]
    [InlineData("this-alias-is-far-too-long-to-be-valid")]
    public async Task UnknownOrMalformedCodeReturnsProblemDetails404(string path)
    {
        var response = await factory.CreateClient(NoRedirects).GetAsync(Relative(path));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task GeneratedCodeInWrongCaseReturns404AndIsNotCounted()
    {
        var code = await CreateLinkAsync("https://example.com/case");
        var wrongCase = code.Any(char.IsUpper) ? code.ToLowerInvariant() : code.ToUpperInvariant();
        Assert.NotEqual(code, wrongCase);

        var response = await factory.CreateClient(NoRedirects).GetAsync(Relative(wrongCase));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await StoredClickCountAsync(code));
    }

    [Fact]
    public async Task RedirectStillWorksWhenClickRecordingFails()
    {
        var code = await CreateLinkAsync("https://example.com/resilient");
        var client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddScoped<IClickRecorder, ThrowingClickRecorder>()))
            .CreateClient(NoRedirects);

        var response = await client.GetAsync(Relative(code));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://example.com/resilient", response.Headers.Location?.OriginalString);
        Assert.Equal(0, await StoredClickCountAsync(code));
    }

    [Fact]
    public async Task HealthRouteIsNotTreatedAsACode()
    {
        var response = await factory.CreateClient(NoRedirects).GetAsync(Relative("health"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    private async Task<string> CreateLinkAsync(string url)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(new Uri("/api/links", UriKind.Relative), new { url });
        response.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("code").GetString()!;
    }

    private async Task<long> StoredClickCountAsync(string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks
            .Where(link => link.Code == code)
            .Select(link => link.ClickCount)
            .SingleAsync();
    }

    private static Uri Relative(string path) => new($"/{path}", UriKind.Relative);

    private sealed class ThrowingClickRecorder : IClickRecorder
    {
        public Task RecordClickAsync(long linkId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("analytics store unavailable");
    }
}