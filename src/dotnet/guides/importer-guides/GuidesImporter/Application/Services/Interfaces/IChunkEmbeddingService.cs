using GuidesImporter.Application.Models;

namespace GuidesImporter.Application.Services.Interfaces;

/// <summary>Embeds every chunk that has no current embedding, recording an EmbeddingRun.</summary>
public interface IChunkEmbeddingService
{
    Task<EmbeddingSummary> EmbedPendingAsync(CancellationToken cancellationToken);
}
