namespace GuidesDbEntities.Models;

/// <summary>Audit row for one execution of the ingestion job.</summary>
public class IngestionRun
{
    public long Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public IngestionRunStatus Status { get; set; }

    /// <summary>Dump file the run read from.</summary>
    public string? Source { get; set; }

    public int ArticlesAdded { get; set; }

    public int ArticlesUpdated { get; set; }

    public int ArticlesSkipped { get; set; }

    public int ArticlesFailed { get; set; }

    public int ChunksWritten { get; set; }

    public string? Error { get; set; }
}

public enum IngestionRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
}
