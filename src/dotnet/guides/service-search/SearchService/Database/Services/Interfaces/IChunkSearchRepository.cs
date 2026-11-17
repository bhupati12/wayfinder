using SearchService.Application.Helpers;

namespace SearchService.Database.Services.Interfaces;

/// <summary>A ranked chunk id with the score of the method that found it.</summary>
public sealed class ChunkHit
{
    public long ChunkId { get; set; }

    public double Score { get; set; }
}

/// <summary>What a search result shows about a chunk.</summary>
public sealed record ChunkDetails(long ChunkId, string ArticleTitle, string Section, string Text, string SourceUrl);

public interface IChunkSearchRepository
{
    /// <summary>Top <paramref name="k"/> chunks embedded by <paramref name="model"/>, by cosine distance to the query vector.</summary>
    Task<IReadOnlyList<ChunkHit>> VectorAsync(
        float[] queryVector,
        string model,
        int k,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Top <paramref name="k"/> chunks by full-text rank. Query words are OR-ed, so chunks matching
    /// more of them rank higher instead of chunks matching all of them being required.
    /// </summary>
    Task<IReadOnlyList<ChunkHit>> KeywordAsync(string query, int k, CancellationToken cancellationToken);

    /// <summary>Vector and keyword top <paramref name="pool"/> each, fused with reciprocal rank fusion.</summary>
    Task<IReadOnlyList<FusedHit>> HybridAsync(
        string query,
        float[] queryVector,
        string model,
        int k,
        int pool,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyDictionary<long, ChunkDetails>> GetDetailsAsync(
        IReadOnlyCollection<long> chunkIds,
        CancellationToken cancellationToken
    );
}
