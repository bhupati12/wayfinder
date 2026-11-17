using GuidesDbEntities.Models;
using GuidesImporter.Application.Models;

namespace GuidesImporter.Database.Services.Interfaces;

public interface IEmbeddingRunRepository
{
    /// <summary>Inserts a run with status Running and returns its id.</summary>
    Task<long> StartAsync(string model, int chunksPending, DateTimeOffset startedAt, CancellationToken cancellationToken);

    /// <summary>Writes the current counts so progress and cost are visible while the run is going.</summary>
    Task UpdateAsync(EmbeddingSummary summary, CancellationToken cancellationToken);

    Task FinishAsync(
        EmbeddingSummary summary,
        IngestionRunStatus status,
        DateTimeOffset finishedAt,
        string? error,
        CancellationToken cancellationToken
    );
}
