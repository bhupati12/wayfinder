using GuidesDbEntities.DAL;
using GuidesImporter.Application.CronJobs;
using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services;
using GuidesImporter.Application.Services.Interfaces;
using GuidesImporter.Database.Services;
using GuidesImporter.Database.Services.Interfaces;

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

        // Jobs: register each ICronJob here, then it can be run by name from the command line.
        services.AddScoped<JobRunner>();
        services.AddScoped<ICronJob, MigrateDatabaseJob>();
        services.AddScoped<ICronJob, IngestGuidesJob>();

        // Phase 2: embedding client and embedding job.
    }

    public static void Configure(WebApplication app)
    {
        app.MapControllers();
        app.MapHealthChecks("/healthz");
    }
}
