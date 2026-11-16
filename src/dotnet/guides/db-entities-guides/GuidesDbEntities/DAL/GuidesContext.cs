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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GuidesContext).Assembly);
    }
}
