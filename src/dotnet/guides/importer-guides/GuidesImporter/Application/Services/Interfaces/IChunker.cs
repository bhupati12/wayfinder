using GuidesImporter.Application.Models;

namespace GuidesImporter.Application.Services.Interfaces;

/// <summary>Splits cleaned article text into retrieval-sized chunks.</summary>
public interface IChunker
{
    /// <summary>
    /// Identifies the chunking rules and settings, for the article's processing signature.
    /// </summary>
    string Signature { get; }

    /// <param name="cleanText">Output of <see cref="IWikitextCleaner.Clean"/>.</param>
    IReadOnlyList<TextChunk> Chunk(string cleanText);
}
