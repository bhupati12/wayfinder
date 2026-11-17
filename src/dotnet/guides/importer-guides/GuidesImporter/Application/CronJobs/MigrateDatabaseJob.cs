using GuidesDbEntities.DAL;
using Microsoft.EntityFrameworkCore;

namespace GuidesImporter.Application.CronJobs;

/// <summary>Applies pending EF Core migrations. Run it before the other jobs after a deploy.</summary>
public sealed class MigrateDatabaseJob : ICronJob
{
    private readonly GuidesContext db;
    private readonly ILogger<MigrateDatabaseJob> logger;

    public MigrateDatabaseJob(GuidesContext db, ILogger<MigrateDatabaseJob> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    public static readonly TimeSpan MigrationCommandTimeout = TimeSpan.FromMinutes(15);

    public string Name => "migrateDatabase";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        logger.LogInformation(
            "Applying {Count} pending migrations: {Migrations}",
            pending.Count,
            pending.Count == 0 ? "none" : string.Join(", ", pending)
        );
        // Schema changes can rewrite whole tables: adding a stored generated column (as
        // AddUnaccentSearch does) rewrites guide_chunks and rebuilds every index on it, the HNSW
        // vector index included. That takes far longer than the short command timeout that suits
        // normal queries; the first local run of that migration failed at the 15 s limit.
        db.Database.SetCommandTimeout(MigrationCommandTimeout);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
