using GuidesImporter.Application.Services.Interfaces;

namespace GuidesImporter.Application.CronJobs;

/// <summary>Embeds chunks that have no embedding, a stale one, or one from another model.</summary>
public sealed class EmbedChunksJob : ICronJob
{
    private readonly IChunkEmbeddingService embeddings;

    public EmbedChunksJob(IChunkEmbeddingService embeddings)
    {
        this.embeddings = embeddings;
    }

    public string Name => "embedChunks";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var summary = await embeddings.EmbedPendingAsync(cancellationToken);
        if (summary.ChunksFailed > 0)
        {
            throw new InvalidOperationException($"{summary.ChunksFailed} chunks failed to embed; see the log.");
        }
    }
}
