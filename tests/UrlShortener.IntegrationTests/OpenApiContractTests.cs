using System.Text.Json;

using Microsoft.AspNetCore.Hosting;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Fails when the API generated from the code drifts from the reviewed contract in
/// specs/002-brownfield-analytics-expiry/contracts/openapi.yaml (v0.2.0, which extends feature 001's;
/// research R14 of feature 001, R8 of feature 002). The expected values below are copied from that
/// file; change both together. <c>GET /api/links/{code}/stats</c> joins with feature 002 task T015. <c>GET /{code}</c> and <c>/health</c> are not in the
/// generated document by design (hidden / not a controller) and are covered by their own tests.
/// </summary>
public sealed class OpenApiContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Dictionary<string, string[]> ExpectedOperations = new()
    {
        ["POST /api/links"] = ["201", "400", "409", "500"],
        ["GET /api/links/{code}"] = ["200", "404"],
    };

    [Fact]
    public async Task GeneratedOperationsAndStatusCodesMatchContract()
    {
        var document = await GetGeneratedDocumentAsync();

        var actual = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => (
                Key: $"{operation.Name.ToUpperInvariant()} {path.Name}",
                Statuses: operation.Value.GetProperty("responses").EnumerateObject()
                    .Select(response => response.Name).Order(StringComparer.Ordinal).ToArray())))
            .ToDictionary(operation => operation.Key, operation => operation.Statuses);

        Assert.Equal(ExpectedOperations.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (operation, statuses) in ExpectedOperations)
        {
            Assert.Equal(statuses, actual[operation]);
        }
    }

    [Theory]
    [InlineData("CreateLinkRequest", new[] { "customAlias", "expiresAt", "url" })]
    [InlineData("LinkResponse", new[] { "clickCount", "code", "createdAtUtc", "expiresAtUtc", "originalUrl", "shortUrl" })]
    public async Task GeneratedSchemasHaveContractFields(string schema, string[] expectedFields)
    {
        var document = await GetGeneratedDocumentAsync();

        var fields = document.GetProperty("components").GetProperty("schemas").GetProperty(schema)
            .GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(expectedFields, fields);
    }

    [Fact]
    public async Task ErrorResponsesAreDocumentedAsProblemJson()
    {
        var document = await GetGeneratedDocumentAsync();
        var create = document.GetProperty("paths").GetProperty("/api/links").GetProperty("post").GetProperty("responses");

        foreach (var status in new[] { "400", "409", "500" })
        {
            Assert.True(
                create.GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _),
                $"POST /api/links {status} must be documented as application/problem+json.");
        }
    }

    private async Task<JsonElement> GetGeneratedDocumentAsync()
    {
        // Swagger is only enabled in Development; this test alone switches environment.
        using var developmentFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        var json = await developmentFactory.CreateClient().GetStringAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}