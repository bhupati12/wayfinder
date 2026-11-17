using System.ComponentModel.DataAnnotations;

namespace SearchService.Application.Options;

/// <summary>Limits and tuning for /api/search, bound from the "Search" section.</summary>
public class SearchOptions
{
    public const string SectionName = "Search";

    [Range(1, 100)]
    public int DefaultK { get; set; } = 10;

    [Range(1, 200)]
    public int MaxK { get; set; } = 50;

    [Range(1, 100)]
    public int MinQueryLength { get; set; } = 2;

    [Range(1, 10_000)]
    public int MaxQueryLength { get; set; } = 500;

    /// <summary>
    /// How many results each of vector and keyword search contributes before fusion. Larger
    /// pools let a chunk that is mid-ranked in both lists rise to the top.
    /// </summary>
    [Range(1, 1000)]
    public int HybridCandidatePool { get; set; } = 50;

    /// <summary>
    /// The k in reciprocal rank fusion, score = sum of 1 / (k + rank). 60 is the value from the
    /// original paper (Cormack et al., 2009); larger values flatten the advantage of top ranks.
    /// </summary>
    [Range(1, 1000)]
    public int RrfK { get; set; } = 60;

    /// <summary>
    /// Weight of the keyword list in the fusion (the vector list's weight is 1). Below 1 trusts
    /// the embedding ranking more; see docs/EVALUATION.md for the measured effect.
    /// </summary>
    [Range(0.0, 2.0)]
    public double KeywordWeight { get; set; } = 1.0;

    /// <summary>
    /// Keyword search first ranks only the chunks that contain every query word, and uses them
    /// when at least this many match; otherwise it ranks every chunk containing any word. Ranking
    /// is the expensive part (each matching tsvector is read and scored), and a common word such
    /// as "get" matches 40% of the corpus, so the all-words pass is far cheaper. 0 always ranks
    /// any-word matches. See docs/LOADTEST.md for the measured effect.
    /// </summary>
    [Range(0, 1000)]
    public int KeywordAllWordsMinResults { get; set; } = 10;

    /// <summary>Added to a keyword score when the article title also matches the query.</summary>
    [Range(0, 10)]
    public double TitleMatchBoost { get; set; } = 0.5;

    /// <summary>
    /// pgvector hnsw.ef_search: candidates the HNSW graph walk keeps. Higher raises recall and
    /// latency. Must be at least the number of results requested.
    /// </summary>
    [Range(1, 1000)]
    public int HnswEfSearch { get; set; } = 100;
}
