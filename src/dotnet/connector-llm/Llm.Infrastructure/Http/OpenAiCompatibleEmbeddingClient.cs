using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Llm.Core.Exceptions;
using Llm.Core.Interfaces;
using Llm.Core.Models;
using Llm.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Polly;

namespace Llm.Infrastructure.Http;

/// <summary>
/// Calls POST {BaseUrl}embeddings in the OpenAI request format. Retries, timeouts and the circuit
/// breaker live in the HttpClient's resilience handler (see LlmServiceCollectionExtensions),
/// so this class only builds requests and validates responses.
/// </summary>
public sealed class OpenAiCompatibleEmbeddingClient : IEmbeddingClient
{
    private const int MaxErrorBodyLength = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient http;
    private readonly EmbeddingOptions options;

    public OpenAiCompatibleEmbeddingClient(HttpClient http, IOptions<EmbeddingOptions> options)
    {
        this.http = http;
        this.options = options.Value;
    }

    public string Model => options.Model;

    public int Dimensions => options.Dimensions;

    public async Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken
    )
    {
        if (inputs.Count == 0)
        {
            return new EmbeddingResult([], 0, TokensReportedByProvider: true);
        }

        var prefix = inputType == EmbeddingInputType.Document ? options.DocumentPrefix : options.QueryPrefix;
        var request = new EmbeddingRequest(
            options.Model,
            inputs.Select(text => prefix + text).ToList(),
            "float",
            options.SendDimensions ? options.Dimensions : null
        );

        using var message = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmProviderException($"Embedding request to {http.BaseAddress} failed: {ex.Message}", null, ex);
        }
        catch (ExecutionRejectedException ex)
        {
            // Polly: attempt or total timeout, or the circuit is open.
            throw new LlmProviderException($"Embedding request rejected: {ex.Message}", null, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (body.Length > MaxErrorBodyLength)
                {
                    body = body[..MaxErrorBodyLength] + "...";
                }

                throw new LlmProviderException(
                    $"Embedding provider returned {(int)response.StatusCode} {response.StatusCode}: {body}",
                    response.StatusCode
                );
            }

            EmbeddingResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(Json, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new LlmProviderException("Embedding provider returned malformed JSON.", response.StatusCode, ex);
            }

            return ToResult(payload, inputs, prefix);
        }
    }

    private EmbeddingResult ToResult(EmbeddingResponse? payload, IReadOnlyList<string> inputs, string prefix)
    {
        if (payload?.Data is null || payload.Data.Count != inputs.Count)
        {
            throw new LlmProviderException(
                $"Expected {inputs.Count} embeddings, got {payload?.Data?.Count ?? 0}."
            );
        }

        var vectors = new float[inputs.Count][];
        foreach (var item in payload.Data)
        {
            if (item.Index < 0 || item.Index >= inputs.Count || vectors[item.Index] is not null)
            {
                throw new LlmProviderException($"Embedding response has an invalid or duplicate index {item.Index}.");
            }

            if (item.Embedding is null || item.Embedding.Length != options.Dimensions)
            {
                throw new LlmProviderException(
                    $"Model {options.Model} returned {item.Embedding?.Length ?? 0} dimensions; {options.Dimensions} are configured."
                );
            }

            vectors[item.Index] = item.Embedding;
        }

        var reported = payload.Usage?.PromptTokens ?? payload.Usage?.TotalTokens;
        var tokens = reported ?? EstimateTokens(inputs, prefix);
        return new EmbeddingResult(vectors, tokens, TokensReportedByProvider: reported is not null);
    }

    /// <summary>About four characters per token for English; only used when the provider reports no usage.</summary>
    private static int EstimateTokens(IReadOnlyList<string> inputs, string prefix) =>
        inputs.Sum(text => (prefix.Length + text.Length + 3) / 4);

    private sealed record EmbeddingRequest(string Model, IReadOnlyList<string> Input, string EncodingFormat, int? Dimensions);

    private sealed record EmbeddingResponse(List<EmbeddingItem>? Data, string? Model, EmbeddingUsage? Usage);

    private sealed record EmbeddingItem(int Index, float[]? Embedding);

    private sealed record EmbeddingUsage(int? PromptTokens, int? TotalTokens);
}
