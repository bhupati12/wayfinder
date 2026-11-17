using Llm.Core.Interfaces;
using Llm.Infrastructure.Http;
using Llm.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Llm.Infrastructure.Extensions;

public static class EmbeddingServiceCollectionExtensions
{
    public const string ResiliencePipelineName = "embeddings";

    /// <summary>
    /// Registers <see cref="IEmbeddingClient"/> as a typed HttpClient with, from the outside in:
    /// a total timeout, retries with exponential backoff and jitter (on timeouts, 408, 429 and
    /// 5xx, honouring Retry-After), a circuit breaker, and a per-attempt timeout.
    /// </summary>
    /// <returns>The HttpClient builder, so tests can swap the primary handler.</returns>
    public static IHttpClientBuilder AddEmbeddingClient(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection(EmbeddingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var builder = services.AddHttpClient<IEmbeddingClient, OpenAiCompatibleEmbeddingClient>(
            (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                // The resilience pipeline owns timeouts; HttpClient's own 100 s limit would cut retries short.
                client.Timeout = Timeout.InfiniteTimeSpan;
            }
        );

        builder.AddResilienceHandler(
            ResiliencePipelineName,
            (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;

                pipeline.AddTimeout(TimeSpan.FromSeconds(options.TotalTimeoutSeconds));
                pipeline.AddRetry(
                    new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = options.MaxRetries,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = options.RetryBaseDelay,
                        MaxDelay = TimeSpan.FromSeconds(60),
                    }
                );
                pipeline.AddCircuitBreaker(
                    new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        MinimumThroughput = 10,
                        SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, options.AttemptTimeoutSeconds * 2)),
                        BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakSeconds),
                    }
                );
                pipeline.AddTimeout(TimeSpan.FromSeconds(options.AttemptTimeoutSeconds));
            }
        );

        return builder;
    }
}
