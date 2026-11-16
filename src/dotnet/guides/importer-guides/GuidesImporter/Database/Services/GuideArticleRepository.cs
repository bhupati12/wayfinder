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
            .Select(a => new { a.PageId, State = new StoredArticleState(a.Id, a.RawTextHash, a.ProcessingSignature) })
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
                await db.Chunks.Where(c => articleIds.Contains(c.ArticleId)).ExecuteDeleteAsync(cancellationToken);

                foreach (var article in updated)
                {
                    var entity = existing[article.PageId];
                    Apply(entity, article, ingestedAt);
                    entity.Chunks = article.Chunks.Select(ToEntity).ToList();
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
                entity.Chunks = article.Chunks.Select(ToEntity).ToList();
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

    private static GuideChunk ToEntity(PreparedChunk chunk) =>
        new()
        {
            SectionHeading = chunk.SectionHeading,
            Position = chunk.Position,
            Text = chunk.Text,
            TokenCount = chunk.TokenCount,
            ContentHash = chunk.ContentHash,
        };
}
