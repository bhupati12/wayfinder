using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GuidesDbEntities.DAL;
using Llm.Core.Interfaces;
using Llm.Infrastructure.Extensions;
using Llm.Infrastructure.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SearchService.Application.Filters;
using SearchService.Application.Options;
using SearchService.Application.Security;
using SearchService.Application.Services;
using SearchService.Application.Services.Interfaces;
using SearchService.Database.Services;
using SearchService.Database.Services.Interfaces;
using StackExchange.Redis;

namespace SearchService;

public static class Startup
{
    public const string AskPolicy = "ask";
    public const string SearchPolicy = "search";
    public const string ReadyTag = "ready";

    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services
            .AddControllers(o => o.Filters.Add<DisableRequestTimeoutOnResultFilter>())
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddOpenApi("v1");
        services.AddSingleton(TimeProvider.System);

        // Errors: problem details everywhere, never a stack trace, always the correlation id.
        services.AddProblemDetails(o =>
            o.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Get(context.HttpContext);
                context.ProblemDetails.Extensions.Remove("exception");
            }
        );
        services.AddExceptionHandler<LlmUnavailableExceptionHandler>();
        services.AddExceptionHandler<DatabaseUnavailableExceptionHandler>();
        services.AddExceptionHandler<FallbackExceptionHandler>();

        // Database: ConnectionStrings:Guides from user secrets (Development) or the environment;
        // pool size and command timeout from the "Database" section.
        services.AddGuidesDb();

        // Search (Phase 3). Queries are embedded by the same model that embedded the chunks.
        services.AddEmbeddingClient(configuration);
        AddValidatedOptions<SearchOptions>(services, configuration, SearchOptions.SectionName);
        services.AddScoped<IChunkSearchRepository, ChunkSearchRepository>();
        services.AddScoped<ISearchService, ChunkSearchService>();

        // Answers (Phase 4).
        services.AddChatClient(configuration);
        AddValidatedOptions<AnswerOptions>(services, configuration, AnswerOptions.SectionName);
        services.AddSingleton<ITokenCounter, TiktokenTokenCounter>();
        services.AddSingleton<IPromptBuilder, PromptBuilder>();
        services.AddScoped<IAnswerService, AnswerService>();

        // Caching and cost cap (Phase 6.4). Redis when configured, otherwise in-process.
        AddValidatedOptions<CachingOptions>(services, configuration, CachingOptions.SectionName);
        AddValidatedOptions<CostCapOptions>(services, configuration, CostCapOptions.SectionName);
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            var redisOptions = ConfigurationOptions.Parse(redis);
            redisOptions.AbortOnConnectFail = false; // start even when Redis is down; reconnect later
            redisOptions.ConnectTimeout = 2000;
            redisOptions.SyncTimeout = 1000;
            redisOptions.AsyncTimeout = 1000;
            // One connection shared by the cache and the usage counters, created on first use.
            var connection = new Lazy<IConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(redisOptions));
            services.AddSingleton(_ => connection.Value);
            services.AddStackExchangeRedisCache(o =>
                o.ConnectionMultiplexerFactory = () => Task.FromResult(connection.Value)
            );
        }

        services.AddSingleton<IResponseCache, ResponseCache>();
        services.AddSingleton<IUsageTracker>(provider => new UsageTracker(
            provider.GetRequiredService<IOptions<CostCapOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<UsageTracker>>(),
            provider.GetService<IConnectionMultiplexer>()
        ));

        // API hardening (Phase 6.2): API keys for /ask, rate limits, request timeouts.
        AddValidatedOptions<AuthOptions>(services, configuration, AuthOptions.SectionName);
        services
            .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName,
                null
            );
        services.AddAuthorization();

        AddValidatedOptions<RateLimitingOptions>(services, configuration, RateLimitingOptions.SectionName);
        var limits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
            ?? new RateLimitingOptions();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AskPolicy, context =>
                FixedWindow(context.User.FindFirstValue(ClaimTypes.Name) ?? "anonymous", limits.AskPermitsPerMinute)
            );
            o.AddPolicy(SearchPolicy, context =>
                FixedWindow(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", limits.SearchPermitsPerMinute)
            );
            o.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                }

                await context
                    .HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(
                        new ProblemDetailsContext
                        {
                            HttpContext = context.HttpContext,
                            ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
                            {
                                Status = StatusCodes.Status429TooManyRequests,
                                Title = "Too many requests.",
                                Detail = "Slow down and retry after the time in the Retry-After header.",
                            },
                        }
                    );
            };
        });

        AddValidatedOptions<ApiTimeoutOptions>(services, configuration, ApiTimeoutOptions.SectionName);
        var timeouts = configuration.GetSection(ApiTimeoutOptions.SectionName).Get<ApiTimeoutOptions>()
            ?? new ApiTimeoutOptions();
        services.AddRequestTimeouts(o =>
        {
            o.AddPolicy(
                SearchPolicy,
                new RequestTimeoutPolicy
                {
                    Timeout = TimeSpan.FromSeconds(timeouts.SearchSeconds),
                    TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
                }
            );
            o.AddPolicy(
                AskPolicy,
                new RequestTimeoutPolicy
                {
                    Timeout = TimeSpan.FromSeconds(timeouts.AskSeconds),
                    TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
                }
            );
        });

        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024);

        // Observability (Phase 6.5): health, traces and metrics.
        var health = services
            .AddHealthChecks()
            .AddDbContextCheck<GuidesDbEntities.DAL.GuidesContext>("postgres", tags: [ReadyTag]);
        if (!string.IsNullOrWhiteSpace(redis))
        {
            health.AddCheck<RedisHealthCheck>("redis", HealthStatus.Degraded, [ReadyTag]);
        }

        services
            .AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(Telemetry.Name))
            .WithTracing(t => t.AddSource(Telemetry.Name).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
            .WithMetrics(m => m.AddMeter(Telemetry.Name).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
        if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            services.AddOpenTelemetry().UseOtlpExporter();
        }
    }

    public static void Configure(WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseRequestTimeouts();

        app.MapOpenApi("/openapi/{documentName}.json");
        app.MapControllers();

        // Liveness: the process is up (no dependencies). Readiness: it can serve traffic.
        app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) });
        app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
    }

    private static void AddValidatedOptions<T>(
        IServiceCollection services,
        IConfiguration configuration,
        string section
    )
        where T : class =>
        services.AddOptions<T>().Bind(configuration.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();

    private static RateLimitPartition<string> FixedWindow(string key, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
}

/// <summary>Readiness check: Redis answers a PING. Reported as degraded, because the service works without its cache.</summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer redis;

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        this.redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"PING {latency.TotalMilliseconds:F0} ms");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis is unreachable.", ex);
        }
    }
}
