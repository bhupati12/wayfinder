using Llm.Infrastructure.Services;
using Llm.Core.Interfaces;
using GuidesDbEntities.Configurations;
using GuidesDbEntities.DAL;
using GuidesImporter.Application.CronJobs;
using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services;
using GuidesImporter.Application.Services.Interfaces;
using GuidesImporter.Database.Services;
using GuidesImporter.Database.Services.Interfaces;
using Llm.Infrastructure.Extensions;
using Llm.Infrastructure.Options;

namespace GuidesImporter;

public static class Startup
{
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddControllers();
        services.AddHealthChecks();
        services.AddSingleton(TimeProvider.System);

        // Database. The connection string comes from user secrets (Development) or the
        // ConnectionStrings__Guides environment variable, never from appsettings.json.
        var connectionString =
            builder.Configuration.GetConnectionString(GuidesDbServiceCollectionExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException(
                "Connection string 'Guides' is not set. For local development run: "
                    + "dotnet user-secrets set \"ConnectionStrings:Guides\" \"<connection string>\" "
                    + "--project src/dotnet/guides/importer-guides/GuidesImporter"
            );
        services.AddGuidesDb(connectionString);

        // Phase 1: ingestion.
        services
            .AddOptions<IngestionOptions>()
            .Bind(builder.Configuration.GetSection(IngestionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ITokenCounter, TiktokenTokenCounter>();
        services.AddSingleton<IWikitextCleaner, WikitextCleaner>();
        services.AddSingleton<IChunker, Chunker>();
        services.AddSingleton<IArticleProcessor, ArticleProcessor>();
        services.AddSingleton<IDumpReader, DumpReader>();
        services.AddScoped<IRegionResolver, RegionResolver>();
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<IGuideArticleRepository, GuideArticleRepository>();
        services.AddScoped<IIngestionRunRepository, IngestionRunRepository>();

        // Phase 2: embeddings. The client's vector length must match the database column.
        services.AddEmbeddingClient(builder.Configuration);
        services
            .AddOptions<EmbeddingOptions>()
            .Validate(
                o => o.Dimensions == GuideChunkConfiguration.EmbeddingDimensions,
                $"Embeddings:Dimensions must be {GuideChunkConfiguration.EmbeddingDimensions} to match the "
                    + "guide_chunks.embedding column; a model with another size needs a migration."
            );
        services
            .AddOptions<EmbedChunksOptions>()
            .Bind(builder.Configuration.GetSection(EmbedChunksOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IChunkEmbeddingService, ChunkEmbeddingService>();
        services.AddScoped<IChunkEmbeddingRepository, ChunkEmbeddingRepository>();
        services.AddScoped<IEmbeddingRunRepository, EmbeddingRunRepository>();

        // Jobs: register each ICronJob here, then it can be run by name from the command line.
        services.AddScoped<JobRunner>();
        services.AddScoped<ICronJob, MigrateDatabaseJob>();
        services.AddScoped<ICronJob, IngestGuidesJob>();
        services.AddScoped<ICronJob, EmbedChunksJob>();
    }

    public static void Configure(WebApplication app)
    {
        app.MapControllers();
        app.MapHealthChecks("/healthz");
    }
}
