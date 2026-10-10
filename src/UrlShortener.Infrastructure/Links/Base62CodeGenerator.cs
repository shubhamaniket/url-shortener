using System.Security.Cryptography;

using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Links;

namespace UrlShortener.Infrastructure.Links;

/// <summary>
/// Generates 7-character Base62 codes from a cryptographically secure source, so codes are
/// neither guessable nor sequential (FR-006, research R1). <c>GetString</c> draws each character
/// uniformly, without modulo bias.
/// </summary>
public sealed class Base62CodeGenerator : IShortCodeGenerator
{
    public string Generate() => RandomNumberGenerator.GetString(GeneratedCode.Alphabet, GeneratedCode.Length);
}