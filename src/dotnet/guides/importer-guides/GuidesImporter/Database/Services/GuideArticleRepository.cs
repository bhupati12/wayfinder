using GuidesDbEntities.DAL;
using GuidesDbEntities.Models;
using GuidesImporter.Application.Models;
using GuidesImporter.Database.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GuidesImporter.Database.Services;

public sealed class GuideArticleRepository : IGuideArticleRepository
{
    private readonly GuidesContext db;

    public GuideArticleRepository(GuidesContext db)
    {
        this.db = db;
    }

    public async Task<IReadOnlyDictionary<long, StoredArticleState>> GetStatesAsync(
        IReadOnlyCollection<long> pageIds,
        CancellationToken cancellationToken
    )
    {
        return await db
            .Articles.AsNoTracking()
            .Where(a => pageIds.Contains(a.PageId))
            .Select(a => new { a.PageId, State = new StoredArticleState(a.Id, a.Title, a.RawTextHash, a.ProcessingSignature) })
            .ToDictionaryAsync(x => x.PageId, x => x.State, cancellationToken);
    }

    public async Task SaveBatchAsync(
        IReadOnlyCollection<PreparedArticle> added,
        IReadOnlyCollection<PreparedArticle> updated,
        DateTimeOffset ingestedAt,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (updated.Count > 0)
            {
                var pageIds = updated.Select(a => a.PageId).ToList();
                var existing = await db
                    .Articles.Where(a => pageIds.Contains(a.PageId))
                    .ToDictionaryAsync(a => a.PageId, cancellationToken);

                var articleIds = existing.Values.Select(a => a.Id).ToList();
                var reusable = await LoadReusableEmbeddingsAsync(articleIds, cancellationToken);
                await db.Chunks.Where(c => articleIds.Contains(c.ArticleId)).ExecuteDeleteAsync(cancellationToken);

                foreach (var article in updated)
                {
                    var entity = existing[article.PageId];
                    Apply(entity, article, ingestedAt);
                    entity.Chunks = article.Chunks.Select(c => ToEntity(c, reusable)).ToList();
                }
            }

            foreach (var article in added)
            {
                var entity = new GuideArticle
                {
                    PageId = article.PageId,
                    Title = article.Title,
                    Url = article.Url,
                    RawTextHash = article.RawTextHash,
                    ProcessingSignature = article.ProcessingSignature,
                };
                Apply(entity, article, ingestedAt);
                entity.Chunks = article.Chunks.Select(c => ToEntity(c, null)).ToList();
                db.Articles.Add(entity);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            // Keep memory flat across batches, and drop half-saved entities after a failure.
            db.ChangeTracker.Clear();
        }
    }

    private static void Apply(GuideArticle entity, PreparedArticle article, DateTimeOffset ingestedAt)
    {
        entity.Title = article.Title;
        entity.Url = article.Url;
        entity.RawTextHash = article.RawTextHash;
        entity.ProcessingSignature = article.ProcessingSignature;
        entity.RevisionTimestamp = article.RevisionTimestamp;
        entity.LastIngestedAt = ingestedAt;
    }

    /// <summary>Current embeddings of the articles' chunks, keyed by content hash.</summary>
    private async Task<Dictionary<string, ReusableEmbedding>> LoadReusableEmbeddingsAsync(
        List<long> articleIds,
        CancellationToken cancellationToken
    )
    {
        var rows = await db
            .Chunks.AsNoTracking()
            .Where(c => articleIds.Contains(c.ArticleId) && c.Embedding != null && c.EmbeddedContentHash == c.ContentHash)
            .Select(c => new ReusableEmbedding(c.ContentHash, c.Embedding!, c.EmbeddingModel!, c.EmbeddedAt))
            .ToListAsync(cancellationToken);

        return rows.DistinctBy(r => r.ContentHash).ToDictionary(r => r.ContentHash);
    }

    private static GuideChunk ToEntity(PreparedChunk chunk, Dictionary<string, ReusableEmbedding>? reusable)
    {
        var entity = new GuideChunk
        {
            SectionHeading = chunk.SectionHeading,
            Position = chunk.Position,
            Text = chunk.Text,
            TokenCount = chunk.TokenCount,
            ContentHash = chunk.ContentHash,
        };

        if (reusable is not null && reusable.TryGetValue(chunk.ContentHash, out var old))
        {
            entity.Embedding = old.Embedding;
            entity.EmbeddingModel = old.Model;
            entity.EmbeddedContentHash = old.ContentHash;
            entity.EmbeddedAt = old.EmbeddedAt;
        }

        return entity;
    }

    private sealed record ReusableEmbedding(string ContentHash, Pgvector.Vector Embedding, string Model, DateTimeOffset? EmbeddedAt);
}
