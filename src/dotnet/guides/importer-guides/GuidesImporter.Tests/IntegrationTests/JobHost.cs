using GuidesDbEntities.DAL;
using GuidesImporter.Application.CronJobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace GuidesImporter.Tests.IntegrationTests;

/// <summary>Starts one pgvector container per test class. Requires Docker.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

/// <summary>
/// Runs a job the way Program does, through the real <see cref="Startup"/> wiring, against the
/// test database and the mini dump fixture.
/// </summary>
internal static class JobHost
{
    public static async Task<int> RunAsync(
        string connectionString,
        string jobName,
        Action<IServiceCollection>? configureServices = null,
        params (string Key, string Value)[] overrides
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory }
        );
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Guides"] = connectionString,
            ["Ingestion:DumpPath"] = TestSupport.FixturePath("mini-dump.xml"),
            ["Ingestion:MinArticleTokens"] = "10",
            ["Ingestion:BatchSize"] = "2",
        };
        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        Startup.ConfigureServices(builder);
        configureServices?.Invoke(builder.Services);

        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<JobRunner>().RunAsync(jobName, CancellationToken.None);
    }

    public static GuidesContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GuidesContext>();
        GuidesDbServiceCollectionExtensions.Configure(options, connectionString);
        return new GuidesContext(options.Options);
    }
}
