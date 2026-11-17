using DomainModels.Guides.Models;
using GuidesDbEntities.DAL;
using GuidesImporter.Database.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GuidesImporter.Database.Services;

public sealed class ChunkTextRepository : IChunkTextRepository
{
    private readonly GuidesContext db;

    public ChunkTextRepository(GuidesContext db)
    {
        this.db = db;
    }

    public async Task<IReadOnlyDictionary<long, string>> GetTextsAsync(
        IReadOnlyCollection<long> chunkIds,
        CancellationToken cancellationToken
    )
    {
        var rows = await db
            .Chunks.AsNoTracking()
            .Where(c => chunkIds.Contains(c.Id))
            .Select(c => new
            {
                c.Id,
                Title = c.Article!.Title,
                c.SectionHeading,
                c.Text,
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => ChunkEmbeddingText.Build(r.Title, r.SectionHeading, r.Text));
    }

    public async Task<(int Articles, int Chunks, string? EmbeddingModel)> DescribeAsync(
        CancellationToken cancellationToken
    )
    {
        var articles = await db.Articles.CountAsync(cancellationToken);
        var chunks = await db.Chunks.CountAsync(cancellationToken);
        var model = await db
            .Chunks.Where(c => c.EmbeddingModel != null)
            .GroupBy(c => c.EmbeddingModel)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefaultAsync(cancellationToken);
        return (articles, chunks, model);
    }
}
