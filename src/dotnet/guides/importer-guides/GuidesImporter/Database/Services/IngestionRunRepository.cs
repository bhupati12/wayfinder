using GuidesDbEntities.DAL;
using GuidesDbEntities.Models;
using GuidesImporter.Application.Models;
using GuidesImporter.Database.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GuidesImporter.Database.Services;

public sealed class IngestionRunRepository : IIngestionRunRepository
{
    private readonly GuidesContext db;

    public IngestionRunRepository(GuidesContext db)
    {
        this.db = db;
    }

    public async Task<long> StartAsync(string source, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var run = new IngestionRun
        {
            StartedAt = startedAt,
            Status = IngestionRunStatus.Running,
            Source = source,
        };
        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(run).State = EntityState.Detached;
        return run.Id;
    }

    public Task UpdateCountsAsync(IngestionSummary summary, CancellationToken cancellationToken) =>
        db
            .IngestionRuns.Where(r => r.Id == summary.RunId)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(r => r.ArticlesAdded, summary.ArticlesAdded)
                        .SetProperty(r => r.ArticlesUpdated, summary.ArticlesUpdated)
                        .SetProperty(r => r.ArticlesSkipped, summary.ArticlesSkipped)
                        .SetProperty(r => r.ArticlesFailed, summary.ArticlesFailed)
                        .SetProperty(r => r.ChunksWritten, summary.ChunksWritten),
                cancellationToken
            );

    public Task FinishAsync(
        IngestionSummary summary,
        IngestionRunStatus status,
        DateTimeOffset finishedAt,
        string? error,
        CancellationToken cancellationToken
    ) =>
        db
            .IngestionRuns.Where(r => r.Id == summary.RunId)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(r => r.ArticlesAdded, summary.ArticlesAdded)
                        .SetProperty(r => r.ArticlesUpdated, summary.ArticlesUpdated)
                        .SetProperty(r => r.ArticlesSkipped, summary.ArticlesSkipped)
                        .SetProperty(r => r.ArticlesFailed, summary.ArticlesFailed)
                        .SetProperty(r => r.ChunksWritten, summary.ChunksWritten)
                        .SetProperty(r => r.Status, status)
                        .SetProperty(r => r.FinishedAt, finishedAt)
                        .SetProperty(r => r.Error, error),
                cancellationToken
            );
}
