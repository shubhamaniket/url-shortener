using Microsoft.Extensions.Options;

using UrlShortener.Application.Options;

namespace UrlShortener.Api.Configuration;

/// <summary>Runs <see cref="ShortLinkOptions.Validate"/> at startup so a bad base URL fails fast.</summary>
internal sealed class ShortLinkOptionsValidator : IValidateOptions<ShortLinkOptions>
{
    public ValidateOptionsResult Validate(string? name, ShortLinkOptions options) =>
        options.Validate() is { } error ? ValidateOptionsResult.Fail(error) : ValidateOptionsResult.Success;
}