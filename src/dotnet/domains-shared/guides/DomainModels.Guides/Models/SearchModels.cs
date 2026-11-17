namespace DomainModels.Guides.Models;

/// <summary>How /api/search ranks chunks.</summary>
public enum SearchMode
{
    /// <summary>Vector and keyword results merged with reciprocal rank fusion.</summary>
    Hybrid,

    /// <summary>Nearest neighbours of the query embedding by cosine distance.</summary>
    Vector,

    /// <summary>PostgreSQL full-text search ranked with ts_rank_cd.</summary>
    Keyword,
}

/// <summary>Response of GET /api/search.</summary>
public sealed record SearchResponse
{
    public const string WikivoyageAttribution =
        "Guide content from Wikivoyage (https://www.wikivoyage.org/), licensed CC BY-SA 4.0 (https://creativecommons.org/licenses/by-sa/4.0/). Each result links to its source article.";

    public required string Query { get; init; }

    public required SearchMode Mode { get; init; }

    public required int K { get; init; }

    /// <summary>True when hybrid search fell back to keyword search because embeddings were unavailable.</summary>
    public bool Degraded { get; init; }

    public string? DegradedReason { get; init; }

    public long ElapsedMilliseconds { get; init; }

    public required IReadOnlyList<SearchResultItem> Results { get; init; }

    public string Attribution { get; init; } = WikivoyageAttribution;
}

/// <summary>One retrieved chunk.</summary>
public sealed record SearchResultItem
{
    public required long ChunkId { get; init; }

    public required string ArticleTitle { get; init; }

    public required string Section { get; init; }

    public required string Text { get; init; }

    /// <summary>Wikivoyage article the chunk comes from.</summary>
    public required string SourceUrl { get; init; }

    /// <summary>
    /// Higher is better. Cosine similarity for vector, ts_rank_cd (plus title boost) for keyword,
    /// reciprocal rank fusion score for hybrid. Scores are comparable within one mode only.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>1-based rank in the vector list, when the chunk was in it.</summary>
    public int? VectorRank { get; init; }

    /// <summary>1-based rank in the keyword list, when the chunk was in it.</summary>
    public int? KeywordRank { get; init; }
}
