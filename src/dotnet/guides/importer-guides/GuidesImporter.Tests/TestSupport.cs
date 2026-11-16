using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services;
using GuidesImporter.Application.Services.Interfaces;

namespace GuidesImporter.Tests;

/// <summary>Counts one token per whitespace-separated word, so expected chunk sizes are easy to reason about.</summary>
internal sealed class WordTokenCounter : ITokenCounter
{
    public string Name => "words";

    public int Count(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

internal static class TestSupport
{
    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string ReadFixture(string name) => File.ReadAllText(FixturePath(name));

    public static Chunker Chunker(ITokenCounter counter, int min, int max, int overlap) =>
        new(
            counter,
            Microsoft.Extensions.Options.Options.Create(
                new IngestionOptions
                {
                    MinChunkTokens = min,
                    MaxChunkTokens = max,
                    OverlapTokens = overlap,
                }
            )
        );

    /// <summary>"W1 w2 ... wN." as one capitalised sentence of exactly <paramref name="words"/> words.</summary>
    public static string Sentence(string prefix, int words)
    {
        var text = string.Join(' ', Enumerable.Range(1, words).Select(i => $"{prefix}{i}")) + ".";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
