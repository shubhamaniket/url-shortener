using System.Collections.Frozen;

namespace UrlShortener.Domain.Links;

/// <summary>
/// A validated custom alias (FR-010, FR-011). Keeps the letter case it was given; uniqueness
/// (ignoring case) is enforced by the data store, not here.
/// </summary>
public sealed class CustomAlias
{
    public const string Field = "customAlias";

    /// <summary>Words that would shadow service routes, compared ignoring case.</summary>
    public static readonly FrozenSet<string> ReservedWords =
        new[] { "api", "health", "swagger", "admin", "static", "assets" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private CustomAlias(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// Validates the alias (surrounding whitespace ignored) and throws
    /// <see cref="LinkValidationException"/> on the first rule it breaks.
    /// </summary>
    public static CustomAlias Parse(string? input)
    {
        var alias = input?.Trim() ?? string.Empty;

        if (alias.Length < ShortLink.MinCodeLength || alias.Length > ShortLink.MaxCodeLength)
        {
            throw Invalid($"The alias must be {ShortLink.MinCodeLength}–{ShortLink.MaxCodeLength} characters long.");
        }

        if (!alias.All(IsAllowed))
        {
            throw Invalid("The alias may only contain letters, digits, '-' and '_'.");
        }

        if (ReservedWords.Contains(alias))
        {
            throw Invalid($"The alias '{alias}' is reserved.");
        }

        return new CustomAlias(alias);
    }

    private static bool IsAllowed(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';

    private static LinkValidationException Invalid(string message) => new(Field, message);
}