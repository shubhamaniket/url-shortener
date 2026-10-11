using System.Text.Json;

using Microsoft.AspNetCore.Hosting;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Fails when the API generated from the code drifts from the reviewed contract in
/// specs/003-reliability-abuse-protection/contracts/openapi.yaml (v0.3.0, which extends features 001
/// and 002; research R14 of feature 001, R8 of features 002 and 003). The expected values below are
/// copied from that file; change both together. <c>GET /{code}</c> and <c>/health</c> are not in the
/// generated document by design (hidden / not a controller) and are covered by their own tests.
/// </summary>
public sealed class OpenApiContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Dictionary<string, string[]> ExpectedOperations = new()
    {
        ["POST /api/links"] = ["201", "400", "409", "429", "500"],
        ["GET /api/links/{code}"] = ["200", "404", "429"],
        ["GET /api/links/{code}/stats"] = ["200", "404", "429"],
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

    // Statistics are aggregates only: no field can carry per-click or per-visitor data (FR-020).
    [InlineData("LinkStatsResponse", new[] { "code", "dailyClicks", "topReferrers", "totalClicks" })]
    [InlineData("DailyClicksResponse", new[] { "clicks", "date" })]
    [InlineData("ReferrerClicksResponse", new[] { "clicks", "source" })]
    public async Task GeneratedSchemasHaveContractFields(string schema, string[] expectedFields)
    {
        var document = await GetGeneratedDocumentAsync();

        var fields = document.GetProperty("components").GetProperty("schemas").GetProperty(schema)
            .GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(expectedFields, fields);
    }

    [Theory]
    [InlineData("url", "https://example.com/some/long/path?x=1")]
    [InlineData("customAlias", "team-offsite")]
    [InlineData("expiresAt", "2099-12-31T23:59:00+05:30")]
    public async Task CreateLinkRequestShowsWorkingExamplesInsteadOfPlaceholders(string field, string example)
    {
        var document = await GetGeneratedDocumentAsync();

        var property = document.GetProperty("components").GetProperty("schemas").GetProperty("CreateLinkRequest")
            .GetProperty("properties").GetProperty(field);

        Assert.Equal(example, property.GetProperty("example").GetString());
    }

    [Fact]
    public async Task EveryOperationHasASingleLineSummary()
    {
        var document = await GetGeneratedDocumentAsync();

        var operations = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => (Key: $"{operation.Name.ToUpperInvariant()} {path.Name}", operation.Value)))
            .ToList();

        Assert.NotEmpty(operations);
        Assert.All(operations, operation =>
        {
            Assert.True(operation.Value.TryGetProperty("summary", out var summary), $"{operation.Key} has no summary.");
            Assert.DoesNotContain('\n', summary.GetString()!);
        });
    }

    [Fact]
    public async Task ErrorResponsesAreDocumentedAsProblemJson()
    {
        var document = await GetGeneratedDocumentAsync();
        var create = document.GetProperty("paths").GetProperty("/api/links").GetProperty("post").GetProperty("responses");

        foreach (var status in new[] { "400", "409", "429", "500" })
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