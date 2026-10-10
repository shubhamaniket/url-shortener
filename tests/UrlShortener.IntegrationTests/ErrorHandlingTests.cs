using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Application.Links;
using UrlShortener.Domain.Links;

namespace UrlShortener.IntegrationTests;

public sealed class ErrorHandlingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task UnmatchedRouteReturnsProblemDetails404()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/api/nope", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task ValidationExceptionReturns400WithFieldErrors()
    {
        var response = await ThrowingClient().GetAsync(new Uri("/test-errors/validation", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal("One or more validation errors occurred.", body.GetProperty("title").GetString());
        var urlErrors = body.GetProperty("errors").GetProperty("url");
        Assert.Equal("The URL must use http or https.", urlErrors[0].GetString());
    }

    [Fact]
    public async Task AliasConflictReturns409()
    {
        var response = await ThrowingClient().GetAsync(new Uri("/test-errors/conflict", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal("Alias already in use", body.GetProperty("title").GetString());
        Assert.Equal("The alias 'team-offsite' is already taken.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnexpectedExceptionReturnsGeneric500WithoutInternals()
    {
        var response = await ThrowingClient().GetAsync(new Uri("/test-errors/unexpected", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", body.GetProperty("detail").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("secret internal detail", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthIsUnaffectedByStatusCodePages()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    private HttpClient ThrowingClient() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(ThrowingController).Assembly)))
            .CreateClient();

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}

/// <summary>Test-only endpoints that throw each mapped exception type.</summary>
[SuppressMessage("Performance", "CA1822", Justification = "MVC actions must be instance methods.")]
[ApiController]
[Route("test-errors")]
public sealed class ThrowingController : ControllerBase
{
    [HttpGet("validation")]
    public IActionResult ThrowValidation() => throw new LinkValidationException("url", "The URL must use http or https.");

    [HttpGet("conflict")]
    public IActionResult ThrowConflict() => throw new AliasAlreadyExistsException("team-offsite");

    [HttpGet("unexpected")]
    public IActionResult ThrowUnexpected() => throw new InvalidOperationException("secret internal detail");
}