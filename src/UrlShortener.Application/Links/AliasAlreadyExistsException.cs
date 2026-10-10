namespace UrlShortener.Application.Links;

/// <summary>The requested custom alias is already in use (compared ignoring letter case).</summary>
public sealed class AliasAlreadyExistsException : Exception
{
    public AliasAlreadyExistsException(string alias)
        : base($"The alias '{alias}' is already taken.")
    {
        Alias = alias;
    }

    public string Alias { get; }
}