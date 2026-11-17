using GuidesDbEntities.Configurations;
using GuidesDbEntities.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using SearchService.Application.Helpers;
using SearchService.Application.Options;
using SearchService.Database.Services.Interfaces;

namespace SearchService.Database.Services;

public sealed class ChunkSearchRepository : IChunkSearchRepository
{
    private readonly GuidesContext db;
    private readonly SearchOptions options;

    public ChunkSearchRepository(GuidesContext db, IOptions<SearchOptions> options)
    {
        this.db = db;
        this.options = options.Value;
    }

    public async Task<IReadOnlyList<ChunkHit>> VectorAsync(
        float[] queryVector,
        string model,
        int k,
        CancellationToken cancellationToken
    )
    {
        var vector = new Vector(queryVector);
        var efSearch = Math.Max(options.HnswEfSearch, k);

        // SET LOCAL only lasts for the transaction, so the setting never leaks to pooled connections.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"SELECT set_config('hnsw.ef_search', {efSearch.ToString(System.Globalization.CultureInfo.InvariantCulture)}, true)",
            cancellationToken
        );

        var hits = await db
            .Chunks.AsNoTracking()
            .Where(c => c.Embedding != null && c.EmbeddingModel == model)
            .OrderBy(c => c.Embedding!.CosineDistance(vector))
            .Take(k)
            .Select(c => new ChunkHit { ChunkId = c.Id, Score = 1 - c.Embedding!.CosineDistance(vector) })
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return hits;
    }

    public async Task<IReadOnlyList<ChunkHit>> KeywordAsync(string query, int k, CancellationToken cancellationToken)
    {
        var minAllWords = options.KeywordAllWordsMinResults;
        if (minAllWords > 0)
        {
            var allWords = await RankKeywordAsync(query, k, allWords: true, cancellationToken);
            if (allWords.Count >= Math.Min(minAllWords, k))
            {
                return allWords;
            }
        }

        return await RankKeywordAsync(query, k, allWords: false, cancellationToken);
    }

    private async Task<List<ChunkHit>> RankKeywordAsync(
        string query,
        int k,
        bool allWords,
        CancellationToken cancellationToken
    )
    {
        var config = GuideChunkConfiguration.FullTextConfiguration;
        var boost = options.TitleMatchBoost;
        var requireAll = allWords;

        // plainto_tsquery stems the words and drops stop words, giving 'eat' & 'ramen' & 'akita'.
        // Swapping & for | makes any word match; ts_rank_cd then rewards chunks with more of them
        // close together. Normalisation 32 maps the rank into [0, 1). Both passes score with the
        // any-word query, so a chunk gets the same score whichever pass finds it; the all-words
        // pass only narrows which chunks are scored. Column aliases are snake_case because the
        // naming convention also applies to SqlQuery result types.
        return await db
            .Database.SqlQuery<ChunkHit>(
                $"""
                SELECT c.id AS chunk_id,
                       (ts_rank_cd(c.search_vector, q.any_words, 32)
                        + CASE WHEN a.title_vector @@ q.any_words THEN {boost} ELSE 0 END)::float8 AS score
                FROM guide_chunks c
                JOIN guide_articles a ON a.id = c.article_id
                CROSS JOIN LATERAL (
                    SELECT p.all_words,
                           NULLIF(replace(p.all_words::text, '&', '|'), '')::tsquery AS any_words
                    FROM (SELECT plainto_tsquery({config}::regconfig, {query}) AS all_words) p
                ) q
                WHERE c.search_vector @@ (CASE WHEN {requireAll} THEN q.all_words ELSE q.any_words END)
                ORDER BY score DESC, c.id
                LIMIT {k}
                """
            )
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FusedHit>> HybridAsync(
        string query,
        float[] queryVector,
        string model,
        int k,
        int pool,
        CancellationToken cancellationToken
    )
    {
        var candidates = Math.Max(pool, k);

        // One DbContext cannot run two queries at once, so the two searches run one after the other.
        var vector = await VectorAsync(queryVector, model, candidates, cancellationToken);
        var keyword = await KeywordAsync(query, candidates, cancellationToken);

        return ReciprocalRankFusion.Fuse(
            vector.Select(h => h.ChunkId).ToList(),
            keyword.Select(h => h.ChunkId).ToList(),
            options.RrfK,
            k,
            options.KeywordWeight
        );
    }

    public async Task<IReadOnlyDictionary<long, ChunkDetails>> GetDetailsAsync(
        IReadOnlyCollection<long> chunkIds,
        CancellationToken cancellationToken
    ) =>
        await db
            .Chunks.AsNoTracking()
            .Where(c => chunkIds.Contains(c.Id))
            .Select(c => new ChunkDetails(c.Id, c.Article!.Title, c.SectionHeading, c.Text, c.Article.Url))
            .ToDictionaryAsync(d => d.ChunkId, cancellationToken);
}
