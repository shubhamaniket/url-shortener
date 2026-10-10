namespace UrlShortener.Domain.Links;

/// <summary>
/// A validated redirect destination (FR-003 to FR-005). <see cref="Value"/> is the parsed,
/// normalized form, so the service always redirects to exactly the URL it validated.
/// </summary>
public sealed class DestinationUrl
{
    public const string Field = "url";

    private DestinationUrl(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// Applies the rules in order (data-model.md, rules 1–7) and throws
    /// <see cref="LinkValidationException"/> on the first failure.
    /// </summary>
    /// <param name="input">The URL as submitted; surrounding whitespace is ignored.</param>
    /// <param name="ownHost">Host of the service's configured public base URL.</param>
    public static DestinationUrl Parse(string? input, string ownHost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownHost);

        var trimmed = input?.Trim();

        // 1. Required.
        if (string.IsNullOrEmpty(trimmed))
        {
            throw Invalid("A URL is required.");
        }

        // 2. Length.
        if (trimmed.Length > ShortLink.MaxUrlLength)
        {
            throw Invalid($"The URL must be at most {ShortLink.MaxUrlLength} characters.");
        }

        // 3 + 4. Absolute URI with an http or https scheme. The scheme check is an allowlist applied
        // after parsing, because inputs like "localhost:8080" parse with a bogus scheme.
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw Invalid(MissingSchemeHint(trimmed) is { } suggestion
                ? $"The URL must start with http:// or https://. Did you mean {suggestion}?"
                : "The URL must be an absolute http or https URL.");
        }

        // 5. Host.
        if (string.IsNullOrEmpty(uri.Host))
        {
            throw Invalid("The URL must include a host.");
        }

        // 6. No user info: "https://google.com@evil.example" really goes to evil.example.
        if (uri.UserInfo.Length > 0)
        {
            throw Invalid("The URL must not contain a username or password.");
        }

        // 7. Not the shortener itself (redirect loops), in any case, scheme, or port.
        if (string.Equals(uri.Host, ownHost, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("The URL must not point to this URL shortener.");
        }

        var normalized = uri.AbsoluteUri;
        if (normalized.Length > ShortLink.MaxUrlLength)
        {
            throw Invalid($"The URL must be at most {ShortLink.MaxUrlLength} characters.");
        }

        return new DestinationUrl(normalized);
    }

    /// <summary>
    /// Suggests an https:// form only when the input has no scheme separator and becomes a valid
    /// http(s) URL with it, so "javascript:alert(1)" never gets a misleading hint.
    /// </summary>
    private static string? MissingSchemeHint(string input)
    {
        if (input.Contains("://", StringComparison.Ordinal) || !char.IsAsciiLetterOrDigit(input[0]))
        {
            return null;
        }

        var candidate = "https://" + input;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Host.Length > 0 && uri.UserInfo.Length == 0
            ? candidate
            : null;
    }

    private static LinkValidationException Invalid(string message) => new(Field, message);
}