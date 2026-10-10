namespace UrlShortener.Application.Abstractions;

public interface IShortCodeGenerator
{
    /// <summary>Returns a new random code in the generated-code format.</summary>
    string Generate();
}