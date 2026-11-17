using System.Diagnostics;
using DomainModels.Guides.Models;
using GuidesDbEntities.Models;
using GuidesImporter.Application.Models;
using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services.Interfaces;
using GuidesImporter.Database.Services.Interfaces;
using Llm.Core.Exceptions;
using Llm.Core.Interfaces;
using Llm.Core.Models;
using Llm.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace GuidesImporter.Application.Services;

/// <summary>
/// Embeds pending chunks in batches. Each batch is saved as soon as it is embedded, and "pending"
/// is computed from the database, so the job can be stopped at any point and the next run picks
/// up where this one left off. It walks chunk ids upwards, so a chunk that failed is not retried
/// in the same run.
/// </summary>
public sealed class ChunkEmbeddingService : IChunkEmbeddingService
{
    private readonly IEmbeddingClient client;
    private readonly IChunkEmbeddingRepository chunks;
    private readonly IEmbeddingRunRepository runs;
    private readonly EmbeddingOptions embeddingOptions;
    private readonly EmbedChunksOptions jobOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<ChunkEmbeddingService> logger;

    public ChunkEmbeddingService(
        IEmbeddingClient client,
        IChunkEmbeddingRepository chunks,
        IEmbeddingRunRepository runs,
        IOptions<EmbeddingOptions> embeddingOptions,
        IOptions<EmbedChunksOptions> jobOptions,
        TimeProvider clock,
        ILogger<ChunkEmbeddingService> logger
    )
    {
        this.client = client;
        this.chunks = chunks;
        this.runs = runs;
        this.embeddingOptions = embeddingOptions.Value;
        this.jobOptions = jobOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<EmbeddingSummary> EmbedPendingAsync(CancellationToken cancellationToken)
    {
        var model = client.Model;
        var pending = await chunks.CountPendingAsync(model, cancellationToken);
        var runId = await runs.StartAsync(model, pending, clock.GetUtcNow(), cancellationToken);
        var summary = new EmbeddingSummary { RunId = runId, Model = model, ChunksPending = pending };
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation(
            "Embedding run {RunId}: {Pending} chunks need an embedding from {Model} (batch size {BatchSize})",
            runId,
            pending,
            model,
            embeddingOptions.BatchSize
        );

        try
        {
            await RunAsync(summary, stopwatch, cancellationToken);
            await runs.FinishAsync(summary, IngestionRunStatus.Succeeded, clock.GetUtcNow(), null, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await runs.FinishAsync(summary, IngestionRunStatus.Cancelled, clock.GetUtcNow(), null, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await runs.FinishAsync(summary, IngestionRunStatus.Failed, clock.GetUtcNow(), ex.Message, CancellationToken.None);
            throw;
        }

        logger.LogInformation(
            "Embedding run {RunId} finished in {Elapsed}: {Embedded} embedded, {Failed} failed, {Requests} requests, {Tokens} input tokens{Estimated}, estimated cost ${Cost}",
            runId,
            stopwatch.Elapsed,
            summary.ChunksEmbedded,
            summary.ChunksFailed,
            summary.Requests,
            summary.InputTokens,
            summary.TokensReportedByProvider ? "" : " (partly estimated)",
            summary.EstimatedCostUsd
        );
        return summary;
    }

    private async Task RunAsync(EmbeddingSummary summary, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var limit = jobOptions.MaxChunks > 0 ? Math.Min(jobOptions.MaxChunks, summary.ChunksPending) : summary.ChunksPending;
        var afterId = 0L;

        while (summary.ChunksEmbedded + summary.ChunksFailed < limit)
        {
            var take = Math.Min(embeddingOptions.BatchSize, limit - summary.ChunksEmbedded - summary.ChunksFailed);
            var batch = await chunks.GetPendingAsync(summary.Model, afterId, take, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            afterId = batch[^1].Id;
            await EmbedBatchAsync(batch, summary, cancellationToken);
            await runs.UpdateAsync(summary, cancellationToken);

            if (summary.Requests % jobOptions.ProgressLogInterval == 0)
            {
                LogProgress(summary, limit, stopwatch);
            }
        }

        LogProgress(summary, limit, stopwatch);
    }

    private async Task EmbedBatchAsync(IReadOnlyList<PendingChunk> batch, EmbeddingSummary summary, CancellationToken cancellationToken)
    {
        try
        {
            var result = await EmbedAsync(batch, summary, cancellationToken);
            await SaveAsync(batch, result, summary, cancellationToken);
        }
        catch (LlmProviderException ex) when (!ex.IsTransient && batch.Count > 1)
        {
            // The provider rejected the request itself (for example an input it cannot take).
            // Embed one at a time so a single bad chunk does not block the others.
            logger.LogWarning(ex, "Batch of {Count} rejected; embedding its chunks one at a time", batch.Count);
            foreach (var chunk in batch)
            {
                try
                {
                    var result = await EmbedAsync([chunk], summary, cancellationToken);
                    await SaveAsync([chunk], result, summary, cancellationToken);
                }
                catch (LlmProviderException single) when (!single.IsTransient)
                {
                    logger.LogError(single, "Could not embed chunk {ChunkId}", chunk.Id);
                    summary.ChunksFailed++;
                }
            }
        }
        catch (LlmProviderException ex) when (!ex.IsTransient)
        {
            logger.LogError(ex, "Could not embed chunk {ChunkId}", batch[0].Id);
            summary.ChunksFailed++;
        }

        // Transient failures (timeouts, 429, 5xx, open circuit) have already been retried by the
        // HttpClient pipeline; they propagate and fail the run, which the next run resumes.
    }

    private async Task<EmbeddingResult> EmbedAsync(IReadOnlyList<PendingChunk> batch, EmbeddingSummary summary, CancellationToken cancellationToken)
    {
        var inputs = batch.Select(c => ChunkEmbeddingText.Build(c.ArticleTitle, c.SectionHeading, c.Text)).ToList();
        summary.Requests++;
        var result = await client.EmbedAsync(inputs, EmbeddingInputType.Document, cancellationToken);

        summary.InputTokens += result.InputTokens;
        summary.TokensReportedByProvider &= result.TokensReportedByProvider;
        summary.EstimatedCostUsd = summary.InputTokens * embeddingOptions.CostPerMillionTokens / 1_000_000m;
        return result;
    }

    private async Task SaveAsync(IReadOnlyList<PendingChunk> batch, EmbeddingResult result, EmbeddingSummary summary, CancellationToken cancellationToken)
    {
        var embeddings = batch.Select((c, i) => new ChunkEmbedding(c.Id, c.ContentHash, result.Vectors[i])).ToList();
        var saved = await chunks.SaveAsync(embeddings, summary.Model, clock.GetUtcNow(), cancellationToken);
        summary.ChunksEmbedded += saved;
        if (saved < batch.Count)
        {
            logger.LogInformation("{Count} chunks changed while being embedded; they stay pending", batch.Count - saved);
        }
    }

    private void LogProgress(EmbeddingSummary summary, int limit, Stopwatch stopwatch)
    {
        var done = summary.ChunksEmbedded + summary.ChunksFailed;
        var rate = done / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
        var remaining = rate > 0 ? TimeSpan.FromSeconds((limit - done) / rate) : TimeSpan.Zero;
        logger.LogInformation(
            "Progress: {Done}/{Limit} chunks ({Rate:F1}/s, about {Remaining:hh\\:mm\\:ss} left), {Tokens} tokens",
            done,
            limit,
            rate,
            remaining,
            summary.InputTokens
        );
    }
}
