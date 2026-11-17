using GuidesDbEntities.Models;
using Microsoft.EntityFrameworkCore;

namespace GuidesImporter.Tests.IntegrationTests;

/// <summary>
/// Runs the real jobs through <see cref="Startup"/> wiring against PostgreSQL, using the
/// mini dump fixture (three articles plus a redirect, a talk page and a disambiguation page).
/// </summary>
public sealed class IngestGuidesJobTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;

    public IngestGuidesJobTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
    }

    [Fact]
    public async Task Ingest_is_idempotent_and_records_each_run()
    {
        var ct = CancellationToken.None;
        Assert.Equal(0, await RunJobAsync("migrateDatabase"));

        // First run: the three articles are added; the other three pages are ignored.
        Assert.Equal(0, await RunJobAsync("ingestGuides"));
        var first = await LatestRunAsync();
        Assert.Equal(IngestionRunStatus.Succeeded, first.Status);
        Assert.Equal(
            (3, 0, 0, 0),
            (first.ArticlesAdded, first.ArticlesUpdated, first.ArticlesSkipped, first.ArticlesFailed)
        );
        Assert.NotNull(first.FinishedAt);

        var chunksAfterFirst = await SnapshotChunksAsync();
        Assert.Equal(first.ChunksWritten, chunksAfterFirst.Count);
        await using (var db = JobHost.CreateContext(postgres.ConnectionString))
        {
            var titles = await db.Articles.OrderBy(a => a.Title).Select(a => a.Title).ToListAsync(ct);
            Assert.Equal(["Aba (prefecture)", "Aioi", "Akajima"], titles);

            var aioi = await db.Articles.SingleAsync(a => a.PageId == 381, ct);
            Assert.Equal("https://en.wikivoyage.org/wiki/Aioi", aioi.Url);
            Assert.Equal(64, aioi.RawTextHash.Length);
        }

        // Second run over the same dump: nothing changes.
        Assert.Equal(0, await RunJobAsync("ingestGuides"));
        var second = await LatestRunAsync();
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(
            (0, 0, 3, 0, 0),
            (
                second.ArticlesAdded,
                second.ArticlesUpdated,
                second.ArticlesSkipped,
                second.ArticlesFailed,
                second.ChunksWritten
            )
        );
        Assert.Equal(chunksAfterFirst, await SnapshotChunksAsync());

        // A title that differs from the dump (a rename with no text change) is updated.
        await using (var db = JobHost.CreateContext(postgres.ConnectionString))
        {
            await db
                .Articles.Where(a => a.PageId == 381)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Title, "Old name"), ct);
        }

        Assert.Equal(0, await RunJobAsync("ingestGuides"));
        var renamed = await LatestRunAsync();
        Assert.Equal((0, 1, 2), (renamed.ArticlesAdded, renamed.ArticlesUpdated, renamed.ArticlesSkipped));
        await using (var db = JobHost.CreateContext(postgres.ConnectionString))
        {
            Assert.Equal("Aioi", await db.Articles.Where(a => a.PageId == 381).Select(a => a.Title).SingleAsync(ct));
        }

        // Changing a chunking setting changes the processing signature, so every article is rebuilt.
        Assert.Equal(
            0,
            await RunJobAsync(
                "ingestGuides",
                ("Ingestion:MaxChunkTokens", "120"),
                ("Ingestion:MinChunkTokens", "60"),
                ("Ingestion:OverlapTokens", "20")
            )
        );
        var third = await LatestRunAsync();
        Assert.Equal(
            (0, 3, 0, 0),
            (third.ArticlesAdded, third.ArticlesUpdated, third.ArticlesSkipped, third.ArticlesFailed)
        );
        var chunksAfterThird = await SnapshotChunksAsync();
        Assert.Equal(third.ChunksWritten, chunksAfterThird.Count);
        Assert.True(chunksAfterThird.Count > chunksAfterFirst.Count);
        Assert.All(chunksAfterThird, c => Assert.InRange(c.TokenCount, 1, 120));
    }

    [Fact]
    public async Task Unknown_job_name_returns_exit_code_2()
    {
        Assert.Equal(2, await RunJobAsync("noSuchJob"));
    }

    private Task<int> RunJobAsync(string jobName, params (string Key, string Value)[] overrides) =>
        JobHost.RunAsync(postgres.ConnectionString, jobName, null, overrides);

    private async Task<IngestionRun> LatestRunAsync()
    {
        await using var db = JobHost.CreateContext(postgres.ConnectionString);
        return await db.IngestionRuns.AsNoTracking().OrderByDescending(r => r.Id).FirstAsync();
    }

    private async Task<
        List<(long Id, long ArticleId, int Position, string ContentHash, int TokenCount)>
    > SnapshotChunksAsync()
    {
        await using var db = JobHost.CreateContext(postgres.ConnectionString);
        var rows = await db
            .Chunks.AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.ArticleId,
                c.Position,
                c.ContentHash,
                c.TokenCount,
            })
            .ToListAsync();
        return rows.Select(r => (r.Id, r.ArticleId, r.Position, r.ContentHash, r.TokenCount)).ToList();
    }
}
