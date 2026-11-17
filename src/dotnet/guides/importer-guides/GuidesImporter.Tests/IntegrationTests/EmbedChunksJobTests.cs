using System.Net;
using GuidesDbEntities.Models;
using Llm.Core.Exceptions;
using Llm.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GuidesImporter.Tests.IntegrationTests;

/// <summary>
/// The embedChunks job against PostgreSQL with pgvector, using a fake embedding client. Each test
/// starts from a freshly ingested mini dump in its own database.
/// </summary>
public sealed class EmbedChunksJobTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;

    public EmbedChunksJobTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
    }

    [Fact]
    public async Task Embeds_every_chunk_once_and_records_tokens_and_cost()
    {
        var connection = await FreshDatabaseAsync();
        var total = await CountChunksAsync(connection);
        var fake = new FakeEmbeddingClient();

        Assert.Equal(0, await EmbedAsync(connection, fake, ("Embeddings:CostPerMillionTokens", "2")));

        Assert.Equal(total, fake.InputsEmbedded);
        Assert.Equal(0, await CountPendingAsync(connection, fake.Model));
        var run = await LatestRunAsync(connection);
        Assert.Equal(IngestionRunStatus.Succeeded, run.Status);
        Assert.Equal((total, total, 0), (run.ChunksPending, run.ChunksEmbedded, run.ChunksFailed));
        Assert.Equal(fake.Calls.Count, run.Requests);
        var expectedTokens = fake.Calls.SelectMany(c => c).Sum(i => i.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(expectedTokens, run.InputTokens);
        Assert.Equal(expectedTokens * 2m / 1_000_000m, run.EstimatedCostUsd);

        // The embedded text carries the article title and section in front of the chunk.
        Assert.Contains(fake.Calls.SelectMany(c => c), i => i.StartsWith("Aioi — Introduction\n\nAioi (相生) is a small port city", StringComparison.Ordinal));

        await using (var db = JobHost.CreateContext(connection))
        {
            var chunk = await db.Chunks.AsNoTracking().FirstAsync();
            Assert.Equal(768, chunk.Embedding!.ToArray().Length);
            Assert.Equal(fake.Model, chunk.EmbeddingModel);
            Assert.Equal(chunk.ContentHash, chunk.EmbeddedContentHash);
            Assert.NotNull(chunk.EmbeddedAt);
        }

        // A second run finds nothing to do.
        var again = new FakeEmbeddingClient();
        Assert.Equal(0, await EmbedAsync(connection, again));
        Assert.Empty(again.Calls);
        Assert.Equal((0, 0), ((await LatestRunAsync(connection)).ChunksPending, (await LatestRunAsync(connection)).ChunksEmbedded));
    }

    [Fact]
    public async Task Re_embeds_stale_chunks_and_chunks_from_another_model()
    {
        var connection = await FreshDatabaseAsync();
        var total = await CountChunksAsync(connection);
        Assert.Equal(0, await EmbedAsync(connection, new FakeEmbeddingClient("model-a")));

        // One chunk's content changed since it was embedded.
        await using (var db = JobHost.CreateContext(connection))
        {
            var id = await db.Chunks.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();
            await db.Chunks.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ContentHash, new string('0', 64)));
        }

        var sameModel = new FakeEmbeddingClient("model-a");
        Assert.Equal(0, await EmbedAsync(connection, sameModel));
        Assert.Equal(1, sameModel.InputsEmbedded);

        // A different model makes every chunk pending.
        var otherModel = new FakeEmbeddingClient("model-b");
        Assert.Equal(0, await EmbedAsync(connection, otherModel));
        Assert.Equal(total, otherModel.InputsEmbedded);
    }

    [Fact]
    public async Task Resumes_after_a_failed_run_without_redoing_saved_batches()
    {
        var connection = await FreshDatabaseAsync();
        var total = await CountChunksAsync(connection);

        var failing = new FakeEmbeddingClient
        {
            OnCall = (call, _) =>
            {
                if (call == 3)
                {
                    throw new LlmProviderException("provider down", HttpStatusCode.ServiceUnavailable);
                }
            },
        };
        Assert.Equal(1, await EmbedAsync(connection, failing, ("Embeddings:BatchSize", "4")));

        var failedRun = await LatestRunAsync(connection);
        Assert.Equal(IngestionRunStatus.Failed, failedRun.Status);
        Assert.Equal(8, failedRun.ChunksEmbedded); // two batches of four were saved before the failure
        Assert.Contains("provider down", failedRun.Error);
        Assert.Equal(total - 8, await CountPendingAsync(connection, failing.Model));

        var healthy = new FakeEmbeddingClient();
        Assert.Equal(0, await EmbedAsync(connection, healthy, ("Embeddings:BatchSize", "4")));
        Assert.Equal(total - 8, healthy.InputsEmbedded);
        Assert.Equal(0, await CountPendingAsync(connection, healthy.Model));
    }

    [Fact]
    public async Task A_rejected_chunk_is_isolated_and_the_rest_are_embedded()
    {
        var connection = await FreshDatabaseAsync();
        var total = await CountChunksAsync(connection);

        var fake = new FakeEmbeddingClient
        {
            OnCall = (_, inputs) =>
            {
                if (inputs.Any(i => i.Contains("Kanjōsan Castle", StringComparison.Ordinal)))
                {
                    throw new LlmProviderException("input rejected", HttpStatusCode.BadRequest);
                }
            },
        };

        // Exit code 1: the run finished, but a chunk failed.
        Assert.Equal(1, await EmbedAsync(connection, fake, ("Embeddings:BatchSize", "4")));

        var run = await LatestRunAsync(connection);
        Assert.Equal(IngestionRunStatus.Succeeded, run.Status);
        Assert.Equal((total - 1, 1), (run.ChunksEmbedded, run.ChunksFailed));
        Assert.Equal(1, await CountPendingAsync(connection, fake.Model));
    }

    [Fact]
    public async Task Re_ingesting_an_article_keeps_embeddings_of_unchanged_chunks()
    {
        var connection = await FreshDatabaseAsync();
        Assert.Equal(0, await EmbedAsync(connection, new FakeEmbeddingClient()));

        // Force every article to be rebuilt although its text is unchanged.
        await using (var db = JobHost.CreateContext(connection))
        {
            await db.Articles.ExecuteUpdateAsync(s => s.SetProperty(a => a.ProcessingSignature, new string('x', 64)));
        }

        Assert.Equal(0, await JobHost.RunAsync(connection, "ingestGuides"));
        await using (var db = JobHost.CreateContext(connection))
        {
            var run = await db.IngestionRuns.OrderByDescending(r => r.Id).FirstAsync();
            Assert.Equal(3, run.ArticlesUpdated);
        }

        var fake = new FakeEmbeddingClient();
        Assert.Equal(0, await EmbedAsync(connection, fake));
        Assert.Empty(fake.Calls);
    }

    private async Task<string> FreshDatabaseAsync()
    {
        // One container per class; each test gets its own database inside it.
        var name = "t" + Guid.NewGuid().ToString("N")[..12];
        await using (var admin = JobHost.CreateContext(postgres.ConnectionString))
        {
#pragma warning disable EF1002 // the name is generated above, not user input
            await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE {name}");
#pragma warning restore EF1002
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = name };
        var connection = builder.ConnectionString;
        Assert.Equal(0, await JobHost.RunAsync(connection, "migrateDatabase"));
        Assert.Equal(0, await JobHost.RunAsync(connection, "ingestGuides"));
        return connection;
    }

    private static Task<int> EmbedAsync(string connection, FakeEmbeddingClient fake, params (string Key, string Value)[] overrides) =>
        JobHost.RunAsync(connection, "embedChunks", services => services.Replace(ServiceDescriptor.Singleton<IEmbeddingClient>(fake)), overrides);

    private static async Task<int> CountChunksAsync(string connection)
    {
        await using var db = JobHost.CreateContext(connection);
        return await db.Chunks.CountAsync();
    }

    private static async Task<int> CountPendingAsync(string connection, string model)
    {
        await using var db = JobHost.CreateContext(connection);
        return await db.Chunks.CountAsync(c => c.Embedding == null || c.EmbeddingModel != model || c.EmbeddedContentHash != c.ContentHash);
    }

    private static async Task<EmbeddingRun> LatestRunAsync(string connection)
    {
        await using var db = JobHost.CreateContext(connection);
        return await db.EmbeddingRuns.AsNoTracking().OrderByDescending(r => r.Id).FirstAsync();
    }
}
