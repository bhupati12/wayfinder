namespace GuidesImporter.Application.Models;

/// <summary>A chunk produced by the chunker, before it is stored.</summary>
/// <param name="SectionHeading">Heading path of the section the chunk starts in.</param>
/// <param name="Position">Zero-based order inside the article.</param>
/// <param name="Text">Plain text of the chunk.</param>
/// <param name="TokenCount">Tokens in <paramref name="Text"/>.</param>
public sealed record TextChunk(string SectionHeading, int Position, string Text, int TokenCount);
