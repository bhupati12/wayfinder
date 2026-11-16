namespace SearchService;

public static class Startup
{
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddControllers();
        services.AddHealthChecks();

        // Phase 3: DbContext, search services (vector, keyword, hybrid).
        // Phase 4: chat client and answer service.
        // Phase 6: caching, rate limiting, OpenTelemetry, exception handling.
    }

    public static void Configure(WebApplication app)
    {
        app.MapControllers();
        app.MapHealthChecks("/healthz");
    }
}
