namespace UrlShortener.Application.Links;

/// <summary>No unique short code could be generated within the allowed number of attempts.</summary>
public sealed class CodeGenerationFailedException : Exception
{
    public CodeGenerationFailedException(int attempts)
        : base($"Could not generate a unique short code after {attempts} attempts.")
    {
        Attempts = attempts;
    }

    public int Attempts { get; }
}