using GuidesDbEntities.DAL;
using GuidesDbEntities.Models;
using GuidesImporter.Application.Models;
using GuidesImporter.Database.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GuidesImporter.Database.Services;

public sealed class EmbeddingRunRepository : IEmbeddingRunRepository
{
    private readonly GuidesContext db;

    public EmbeddingRunRepository(GuidesContext db)
    {
        this.db = db;
    }

    public async Task<long> StartAsync(string model, int chunksPending, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var run = new EmbeddingRun
        {
            StartedAt = startedAt,
            Status = IngestionRunStatus.Running,
            Model = model,
            ChunksPending = chunksPending,
        };
        db.EmbeddingRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(run).State = EntityState.Detached;
        return run.Id;
    }

    public Task UpdateAsync(EmbeddingSummary summary, CancellationToken cancellationToken) =>
        db
            .EmbeddingRuns.Where(r => r.Id == summary.RunId)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(r => r.ChunksEmbedded, summary.ChunksEmbedded)
                        .SetProperty(r => r.ChunksFailed, summary.ChunksFailed)
                        .SetProperty(r => r.Requests, summary.Requests)
                        .SetProperty(r => r.InputTokens, summary.InputTokens)
                        .SetProperty(r => r.TokensReportedByProvider, summary.TokensReportedByProvider)
                        .SetProperty(r => r.EstimatedCostUsd, summary.EstimatedCostUsd),
                cancellationToken
            );

    public Task FinishAsync(
        EmbeddingSummary summary,
        IngestionRunStatus status,
        DateTimeOffset finishedAt,
        string? error,
        CancellationToken cancellationToken
    ) =>
        db
            .EmbeddingRuns.Where(r => r.Id == summary.RunId)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(r => r.ChunksEmbedded, summary.ChunksEmbedded)
                        .SetProperty(r => r.ChunksFailed, summary.ChunksFailed)
                        .SetProperty(r => r.Requests, summary.Requests)
                        .SetProperty(r => r.InputTokens, summary.InputTokens)
                        .SetProperty(r => r.TokensReportedByProvider, summary.TokensReportedByProvider)
                        .SetProperty(r => r.EstimatedCostUsd, summary.EstimatedCostUsd)
                        .SetProperty(r => r.Status, status)
                        .SetProperty(r => r.FinishedAt, finishedAt)
                        .SetProperty(r => r.Error, error),
                cancellationToken
            );
}
