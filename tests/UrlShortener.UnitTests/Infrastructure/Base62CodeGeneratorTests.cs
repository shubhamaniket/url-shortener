using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure.Links;

namespace UrlShortener.UnitTests.Infrastructure;

public class Base62CodeGeneratorTests
{
    private readonly Base62CodeGenerator _generator = new();

    [Fact]
    public void GeneratesValidGeneratedCodes()
    {
        for (var i = 0; i < 1_000; i++)
        {
            var code = _generator.Generate();

            Assert.Equal(GeneratedCode.Length, code.Length);
            Assert.True(GeneratedCode.IsValid(code), $"'{code}' is not a valid generated code.");
            Assert.All(code, c => Assert.Contains(c, GeneratedCode.Alphabet));
        }
    }

    [Fact]
    public void ThousandCodesAreDistinct()
    {
        var codes = Enumerable.Range(0, 1_000).Select(_ => _generator.Generate()).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void UsesWholeAlphabet()
    {
        // 20,000 codes x 7 chars = 140,000 draws over 62 symbols; every symbol is expected
        // ~2,258 times, so a missing symbol would mean a broken alphabet, not bad luck.
        var seen = new HashSet<char>();
        for (var i = 0; i < 20_000; i++)
        {
            seen.UnionWith(_generator.Generate());
        }

        Assert.Equal(GeneratedCode.Alphabet.Length, seen.Count);
    }
}