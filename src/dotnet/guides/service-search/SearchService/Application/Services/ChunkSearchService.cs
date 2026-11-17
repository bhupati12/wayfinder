using System.Diagnostics;
using DomainModels.Guides.Models;
using Llm.Core.Exceptions;
using Llm.Core.Interfaces;
using Llm.Core.Models;
using Microsoft.Extensions.Options;
using SearchService.Application.Helpers;
using SearchService.Application.Options;
using SearchService.Application.Services.Interfaces;
using SearchService.Database.Services.Interfaces;

namespace SearchService.Application.Services;

public sealed class ChunkSearchService : ISearchService
{
    private readonly IChunkSearchRepository repository;
    private readonly IEmbeddingClient embeddings;
    private readonly SearchOptions options;
    private readonly ILogger<ChunkSearchService> logger;

    public ChunkSearchService(
        IChunkSearchRepository repository,
        IEmbeddingClient embeddings,
        IOptions<SearchOptions> options,
        ILogger<ChunkSearchService> logger
    )
    {
        this.repository = repository;
        this.embeddings = embeddings;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<SearchResponse> SearchAsync(string query, SearchMode mode, int k, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        query = query.Trim();

        IReadOnlyList<FusedHit> hits;
        string? degradedReason = null;

        switch (mode)
        {
            case SearchMode.Vector:
            {
                var vector = await EmbedQueryAsync(query, cancellationToken);
                hits = Ranked(await repository.VectorAsync(vector, embeddings.Model, k, cancellationToken), isVector: true);
                break;
            }

            case SearchMode.Keyword:
                hits = Ranked(await repository.KeywordAsync(query, k, cancellationToken), isVector: false);
                break;

            default:
            {
                float[]? vector = null;
                try
                {
                    vector = await EmbedQueryAsync(query, cancellationToken);
                }
                catch (LlmProviderException ex)
                {
                    // Keyword search needs no model, so search keeps working when the embedding
                    // provider is down; the response says so.
                    logger.LogWarning(ex, "Query embedding failed; hybrid search falls back to keyword search");
                    degradedReason = "Embedding provider unavailable; results are keyword search only.";
                }

                hits = vector is null
                    ? Ranked(await repository.KeywordAsync(query, k, cancellationToken), isVector: false)
                    : await repository.HybridAsync(query, vector, embeddings.Model, k, options.HybridCandidatePool, cancellationToken);
                break;
            }
        }

        var details = await repository.GetDetailsAsync(hits.Select(h => h.ChunkId).ToList(), cancellationToken);
        var results = hits
            .Where(h => details.ContainsKey(h.ChunkId))
            .Select(h =>
            {
                var d = details[h.ChunkId];
                return new SearchResultItem
                {
                    ChunkId = h.ChunkId,
                    ArticleTitle = d.ArticleTitle,
                    Section = d.Section,
                    Text = d.Text,
                    SourceUrl = d.SourceUrl,
                    Score = Math.Round(h.Score, 6),
                    VectorRank = h.VectorRank,
                    KeywordRank = h.KeywordRank,
                };
            })
            .ToList();

        return new SearchResponse
        {
            Query = query,
            Mode = mode,
            K = k,
            Degraded = degradedReason is not null,
            DegradedReason = degradedReason,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            Results = results,
        };
    }

    private async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        var result = await embeddings.EmbedAsync([query], EmbeddingInputType.Query, cancellationToken);
        return result.Vectors[0];
    }

    private static IReadOnlyList<FusedHit> Ranked(IReadOnlyList<ChunkHit> hits, bool isVector) =>
        hits.Select((h, i) => new FusedHit(h.ChunkId, h.Score, isVector ? i + 1 : null, isVector ? null : i + 1)).ToList();
}
