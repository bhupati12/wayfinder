using GuidesDbEntities.Models;
using GuidesImporter.Application.Models;

namespace GuidesImporter.Database.Services.Interfaces;

public interface IIngestionRunRepository
{
    /// <summary>Inserts a run with status Running and returns its id.</summary>
    Task<long> StartAsync(string source, DateTimeOffset startedAt, CancellationToken cancellationToken);

    /// <summary>Writes the current counts so progress is visible while the run is going.</summary>
    Task UpdateCountsAsync(IngestionSummary summary, CancellationToken cancellationToken);

    Task FinishAsync(
        IngestionSummary summary,
        IngestionRunStatus status,
        DateTimeOffset finishedAt,
        string? error,
        CancellationToken cancellationToken
    );
}
