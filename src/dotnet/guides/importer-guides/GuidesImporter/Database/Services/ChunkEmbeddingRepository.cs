using GuidesDbEntities.DAL;
using GuidesDbEntities.Models;
using GuidesImporter.Application.Models;
using GuidesImporter.Database.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace GuidesImporter.Database.Services;

public sealed class ChunkEmbeddingRepository : IChunkEmbeddingRepository
{
    private readonly GuidesContext db;

    public ChunkEmbeddingRepository(GuidesContext db)
    {
        this.db = db;
    }

    public Task<int> CountPendingAsync(string model, CancellationToken cancellationToken) =>
        Pending(model).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<PendingChunk>> GetPendingAsync(
        string model,
        long afterId,
        int take,
        CancellationToken cancellationToken
    ) =>
        await Pending(model)
            .Where(c => c.Id > afterId)
            .OrderBy(c => c.Id)
            .Take(take)
            .Select(c => new PendingChunk(c.Id, c.Article!.Title, c.SectionHeading, c.Text, c.ContentHash))
            .ToListAsync(cancellationToken);

    public async Task<int> SaveAsync(
        IReadOnlyList<ChunkEmbedding> embeddings,
        string model,
        DateTimeOffset embeddedAt,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var updated = 0;
        foreach (var embedding in embeddings)
        {
            var vector = new Vector(embedding.Vector);
            updated += await db
                .Chunks.Where(c => c.Id == embedding.ChunkId && c.ContentHash == embedding.ContentHash)
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(c => c.Embedding, vector)
                            .SetProperty(c => c.EmbeddingModel, model)
                            .SetProperty(c => c.EmbeddedContentHash, embedding.ContentHash)
                            .SetProperty(c => c.EmbeddedAt, embeddedAt),
                    cancellationToken
                );
        }

        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    private IQueryable<GuideChunk> Pending(string model) =>
        db
            .Chunks.AsNoTracking()
            .Where(c => c.Embedding == null || c.EmbeddingModel != model || c.EmbeddedContentHash != c.ContentHash);
}
