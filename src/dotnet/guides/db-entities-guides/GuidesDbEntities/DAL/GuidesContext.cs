using GuidesDbEntities.Models;
using Microsoft.EntityFrameworkCore;

namespace GuidesDbEntities.DAL;

public class GuidesContext : DbContext
{
    public GuidesContext(DbContextOptions<GuidesContext> options)
        : base(options) { }

    public DbSet<GuideArticle> Articles => Set<GuideArticle>();

    public DbSet<GuideChunk> Chunks => Set<GuideChunk>();

    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();

    public DbSet<EmbeddingRun> EmbeddingRuns => Set<EmbeddingRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GuidesContext).Assembly);
    }
}
