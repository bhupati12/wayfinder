namespace GuidesImporter.Application.Models;

/// <summary>An article that has been cleaned, chunked and hashed, ready to upsert.</summary>
public sealed record PreparedArticle(
    long PageId,
    string Title,
    string Url,
    string RawTextHash,
    string ProcessingSignature,
    DateTimeOffset? RevisionTimestamp,
    IReadOnlyList<PreparedChunk> Chunks
)
{
    public int TotalTokens => Chunks.Sum(c => c.TokenCount);
}

public sealed record PreparedChunk(
    string SectionHeading,
    int Position,
    string Text,
    int TokenCount,
    string ContentHash
);
