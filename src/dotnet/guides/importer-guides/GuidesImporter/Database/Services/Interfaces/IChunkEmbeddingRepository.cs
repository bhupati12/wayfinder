using GuidesImporter.Application.Models;

namespace GuidesImporter.Database.Services.Interfaces;

/// <summary>
/// Reads chunks that need an embedding and stores embeddings. A chunk needs one when it has none,
/// when it was embedded by a different model, or when its content hash changed since.
/// </summary>
public interface IChunkEmbeddingRepository
{
    Task<int> CountPendingAsync(string model, CancellationToken cancellationToken);

    /// <summary>Pending chunks with an id greater than <paramref name="afterId"/>, in id order.</summary>
    Task<IReadOnlyList<PendingChunk>> GetPendingAsync(
        string model,
        long afterId,
        int take,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Stores the embeddings in one transaction. A chunk whose content hash changed in the
    /// meantime is left alone, so a concurrent re-ingest never gets a stale vector. Returns the
    /// number of chunks updated.
    /// </summary>
    Task<int> SaveAsync(
        IReadOnlyList<ChunkEmbedding> embeddings,
        string model,
        DateTimeOffset embeddedAt,
        CancellationToken cancellationToken
    );
}
