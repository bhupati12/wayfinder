namespace GuidesImporter.Application.Models;

/// <summary>Counts and cost of an embedding run.</summary>
public sealed class EmbeddingSummary
{
    public long RunId { get; init; }

    public required string Model { get; init; }

    public int ChunksPending { get; set; }

    public int ChunksEmbedded { get; set; }

    public int ChunksFailed { get; set; }

    public int Requests { get; set; }

    public long InputTokens { get; set; }

    public bool TokensReportedByProvider { get; set; } = true;

    public decimal EstimatedCostUsd { get; set; }
}

/// <summary>A chunk that needs an embedding, with what is needed to build its embedding text.</summary>
public sealed record PendingChunk(long Id, string ArticleTitle, string SectionHeading, string Text, string ContentHash);

/// <summary>An embedding to store, guarded by the content hash it was made from.</summary>
public sealed record ChunkEmbedding(long ChunkId, string ContentHash, float[] Vector);
