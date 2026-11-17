namespace GuidesDbEntities.Models;

/// <summary>Audit row for one execution of the embedding job, including what it cost.</summary>
public class EmbeddingRun
{
    public long Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public IngestionRunStatus Status { get; set; }

    public required string Model { get; set; }

    /// <summary>Chunks that needed an embedding when the run started.</summary>
    public int ChunksPending { get; set; }

    public int ChunksEmbedded { get; set; }

    public int ChunksFailed { get; set; }

    public int Requests { get; set; }

    /// <summary>Input tokens billed by the provider (or estimated when it reports none).</summary>
    public long InputTokens { get; set; }

    /// <summary>False when at least one request reported no usage and its tokens were estimated.</summary>
    public bool TokensReportedByProvider { get; set; } = true;

    /// <summary>InputTokens times the configured price per million tokens, in US dollars.</summary>
    public decimal EstimatedCostUsd { get; set; }

    public string? Error { get; set; }
}
