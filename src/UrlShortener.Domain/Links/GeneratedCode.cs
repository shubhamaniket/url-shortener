namespace UrlShortener.Domain.Links;

/// <summary>Format of system-generated short codes: 7 characters from the Base62 alphabet.</summary>
public static class GeneratedCode
{
    public const int Length = 7;

    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static bool IsValid(string? value) =>
        value is { Length: Length } && value.All(char.IsAsciiLetterOrDigit);
}