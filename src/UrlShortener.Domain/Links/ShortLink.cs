namespace UrlShortener.Domain.Links;

/// <summary>A mapping from a short code (generated or custom alias) to an original URL.</summary>
/// <remarks>
/// Format rules for custom aliases and destination URLs are validated by the caller before
/// creation (see <c>CustomAlias</c> and <c>DestinationUrl</c>); this entity guards only the
/// invariants it owns. Links are immutable apart from <see cref="ClickCount"/>, which is
/// increased in the data store by the click recorder.
/// </remarks>
public sealed class ShortLink
{
    public const int MinCodeLength = 3;
    public const int MaxCodeLength = 30;
    public const int MaxUrlLength = 2048;

    // Used by EF Core when materializing from the database.
    private ShortLink()
    {
        Code = string.Empty;
        NormalizedCode = string.Empty;
        OriginalUrl = string.Empty;
    }

    private ShortLink(string code, bool isCustomAlias, string originalUrl, DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalUrl);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(originalUrl.Length, MaxUrlLength, nameof(originalUrl));
        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Creation time must be UTC.", nameof(createdAtUtc));
        }

        Code = code;
        NormalizedCode = Normalize(code);
        IsCustomAlias = isCustomAlias;
        OriginalUrl = originalUrl;
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }

    /// <summary>The code as created; a custom alias keeps its original letter case.</summary>
    public string Code { get; private set; }

    /// <summary>Lower-cased <see cref="Code"/>; unique across all links.</summary>
    public string NormalizedCode { get; private set; }

    public bool IsCustomAlias { get; private set; }

    public string OriginalUrl { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public long ClickCount { get; private set; }

    public static ShortLink CreateWithGeneratedCode(string code, string originalUrl, DateTime createdAtUtc)
    {
        if (!GeneratedCode.IsValid(code))
        {
            throw new ArgumentException(
                $"A generated code must be {GeneratedCode.Length} Base62 characters.", nameof(code));
        }

        return new ShortLink(code, isCustomAlias: false, originalUrl, createdAtUtc);
    }

    public static ShortLink CreateWithCustomAlias(string alias, string originalUrl, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentOutOfRangeException.ThrowIfLessThan(alias.Length, MinCodeLength, nameof(alias));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(alias.Length, MaxCodeLength, nameof(alias));

        return new ShortLink(alias, isCustomAlias: true, originalUrl, createdAtUtc);
    }

    /// <summary>The form used for uniqueness and lookup.</summary>
    public static string Normalize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.ToLowerInvariant();
    }

    /// <summary>
    /// Whether a requested code resolves to this link: custom aliases match in any letter case,
    /// generated codes only in exact case.
    /// </summary>
    public bool Matches(string requestedCode)
    {
        ArgumentNullException.ThrowIfNull(requestedCode);

        return IsCustomAlias
            ? string.Equals(NormalizedCode, Normalize(requestedCode), StringComparison.Ordinal)
            : string.Equals(Code, requestedCode, StringComparison.Ordinal);
    }
}