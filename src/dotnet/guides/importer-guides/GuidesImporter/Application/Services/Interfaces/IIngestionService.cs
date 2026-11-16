using GuidesImporter.Application.Models;

namespace GuidesImporter.Application.Services.Interfaces;

/// <summary>Reads the dump and upserts articles and chunks, recording an IngestionRun.</summary>
public interface IIngestionService
{
    Task<IngestionSummary> IngestAsync(CancellationToken cancellationToken);
}
