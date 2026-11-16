using GuidesImporter.Application.CronJobs;

namespace GuidesImporter;

public static class Startup
{
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddControllers();
        services.AddHealthChecks();

        // Jobs: register each ICronJob here, then it can be run by name from the command line.
        services.AddScoped<JobRunner>();

        // Phase 1: DbContext, options binding, ingestion services.
        // Phase 2: embedding client and embedding job.
    }

    public static void Configure(WebApplication app)
    {
        app.MapControllers();
        app.MapHealthChecks("/healthz");
    }
}
