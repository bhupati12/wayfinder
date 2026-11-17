namespace GuidesImporter.Application.Models;

/// <summary>Counts reported at the end of an ingestion run.</summary>
public sealed class IngestionSummary
{
    public long RunId { get; init; }

    /// <summary>Pages read from the dump, of any kind.</summary>
    public int PagesRead { get; set; }

    /// <summary>Pages that are not eligible: redirects, other namespaces, outside the region, too short.</summary>
    public int PagesIgnored { get; set; }

    public int ArticlesAdded { get; set; }

    public int ArticlesUpdated { get; set; }

    /// <summary>Articles whose text and processing settings are unchanged since the last run.</summary>
    public int ArticlesSkipped { get; set; }

    public int ArticlesFailed { get; set; }

    public int ChunksWritten { get; set; }

    public int ArticlesProcessed => ArticlesAdded + ArticlesUpdated + ArticlesSkipped + ArticlesFailed;
}
