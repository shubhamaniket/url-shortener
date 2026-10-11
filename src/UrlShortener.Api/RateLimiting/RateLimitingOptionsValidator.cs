using Microsoft.Extensions.Options;

namespace UrlShortener.Api.RateLimiting;

/// <summary>Runs <see cref="RateLimitingOptions.Validate"/> at startup so a bad budget fails fast (FR-010).</summary>
internal sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options) =>
        options.Validate() is { Count: > 0 } errors ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
}