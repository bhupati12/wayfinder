using System.Text.Json.Serialization;
using GuidesDbEntities.DAL;
using Llm.Core.Interfaces;
using Llm.Infrastructure.Extensions;
using Llm.Infrastructure.Services;
using SearchService.Application.Options;
using SearchService.Application.Services;
using SearchService.Application.Services.Interfaces;
using SearchService.Database.Services;
using SearchService.Database.Services.Interfaces;

namespace SearchService;

public static class Startup
{
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services
            .AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddHealthChecks();
        services.AddProblemDetails();
        services.AddExceptionHandler<LlmUnavailableExceptionHandler>();

        // Database: ConnectionStrings:Guides from user secrets (Development) or the environment.
        services.AddGuidesDb();

        // Phase 3: search. Queries are embedded by the same model that embedded the chunks.
        services.AddEmbeddingClient(builder.Configuration);
        services
            .AddOptions<SearchOptions>()
            .Bind(builder.Configuration.GetSection(SearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IChunkSearchRepository, ChunkSearchRepository>();
        services.AddScoped<ISearchService, ChunkSearchService>();

        // Phase 4: grounded answers.
        services.AddChatClient(builder.Configuration);
        services
            .AddOptions<AnswerOptions>()
            .Bind(builder.Configuration.GetSection(AnswerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<ITokenCounter, TiktokenTokenCounter>();
        services.AddSingleton<IPromptBuilder, PromptBuilder>();
        services.AddScoped<IAnswerService, AnswerService>();

        // Phase 6: caching, rate limiting, OpenTelemetry.
    }

    public static void Configure(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.MapControllers();
        app.MapHealthChecks("/healthz");
    }
}
