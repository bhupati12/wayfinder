using Llm.Core.Interfaces;
using Llm.Infrastructure.Http;
using Llm.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Llm.Infrastructure.Extensions;

public static class LlmServiceCollectionExtensions
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

        builder.AddResilienceHandler(ResiliencePipelineName, (pipeline, context) =>
        {
            var o = context.ServiceProvider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
            AddStandardStrategies(
                pipeline,
                o.TotalTimeoutSeconds,
                o.AttemptTimeoutSeconds,
                o.MaxRetries,
                o.RetryBaseDelay,
                o.CircuitBreakSeconds
            );
        });

        return builder;
    }

    /// <summary>
    /// Registers <see cref="IChatClient"/> with the same resilience strategies as the embedding
    /// client. The strategies cover the time until response headers arrive.
    /// </summary>
    public static IHttpClientBuilder AddChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ChatOptions>()
            .Bind(configuration.GetSection(ChatOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var builder = services.AddHttpClient<IChatClient, OpenAiCompatibleChatClient>(
            (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<ChatOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan;
            }
        );

        builder.AddResilienceHandler("chat", (pipeline, context) =>
        {
            var o = context.ServiceProvider.GetRequiredService<IOptions<ChatOptions>>().Value;
            AddStandardStrategies(
                pipeline,
                o.TotalTimeoutSeconds,
                o.AttemptTimeoutSeconds,
                o.MaxRetries,
                o.RetryBaseDelay,
                o.CircuitBreakSeconds
            );
        });

        return builder;
    }

    private static void AddStandardStrategies(
        ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
        int totalTimeoutSeconds,
        int attemptTimeoutSeconds,
        int maxRetries,
        TimeSpan retryBaseDelay,
        int circuitBreakSeconds
    )
    {
        pipeline.AddTimeout(TimeSpan.FromSeconds(totalTimeoutSeconds));
        if (maxRetries > 0)
        {
            pipeline.AddRetry(
                new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = maxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = retryBaseDelay,
                    MaxDelay = TimeSpan.FromSeconds(60),
                }
            );
        }

        pipeline.AddCircuitBreaker(
            new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, attemptTimeoutSeconds * 2)),
                BreakDuration = TimeSpan.FromSeconds(circuitBreakSeconds),
            }
        );
        pipeline.AddTimeout(TimeSpan.FromSeconds(attemptTimeoutSeconds));
    }
}
