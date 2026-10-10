using UrlShortener.Application.Abstractions;

namespace UrlShortener.UnitTests.Fakes;

/// <summary>Returns the given codes in order, repeating the last one; counts calls.</summary>
internal sealed class ScriptedCodeGenerator(params string[] codes) : IShortCodeGenerator
{
    public int Calls { get; private set; }

    public string Generate() => codes[Math.Min(Calls++, codes.Length - 1)];
}