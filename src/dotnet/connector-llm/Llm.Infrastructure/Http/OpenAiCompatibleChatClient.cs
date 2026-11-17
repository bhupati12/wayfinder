using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
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
/// Calls POST {BaseUrl}chat/completions in the OpenAI format, plain or streamed as server-sent
/// events. Retries and timeouts live in the HttpClient's resilience handler and cover the time
/// until response headers arrive; a streamed body is read outside it, so tokens already shown
/// are never replayed.
/// </summary>
public sealed class OpenAiCompatibleChatClient : IChatClient
{
    private const int MaxErrorBodyLength = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient http;
    private readonly ChatOptions options;

    public OpenAiCompatibleChatClient(HttpClient http, IOptions<ChatOptions> options)
    {
        this.http = http;
        this.options = options.Value;
    }

    public string Model => options.Model;

    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, stream: false, cancellationToken);

        CompletionResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<CompletionResponse>(Json, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new LlmProviderException("Chat provider returned malformed JSON.", response.StatusCode, ex);
        }

        var choice = payload?.Choices?.FirstOrDefault();
        var text = choice?.Message?.Content ?? throw new LlmProviderException("Chat provider returned no message.");
        var usage = payload!.Usage is { } u
            ? new ChatUsage(u.PromptTokens ?? 0, u.CompletionTokens ?? 0, Estimated: false)
            : Estimate(request, text);

        return new ChatCompletion(text, payload.Model ?? options.Model, choice.FinishReason, usage);
    }

    public async IAsyncEnumerable<ChatStreamUpdate> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        using var response = await SendAsync(request, stream: true, cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body, Encoding.UTF8);

        var text = new StringBuilder();
        string? finishReason = null;
        ChatUsage? usage = null;

        while (await ReadLineAsync(reader, cancellationToken) is { } line)
        {
            // SSE: "data: {json}" lines separated by blank lines; "data: [DONE]" ends the stream.
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data == "[DONE]")
            {
                break;
            }

            var chunk = ParseChunk(data);
            if (chunk.Usage is { } u)
            {
                usage = new ChatUsage(u.PromptTokens ?? 0, u.CompletionTokens ?? 0, Estimated: false);
            }

            var choice = chunk.Choices?.FirstOrDefault();
            finishReason = choice?.FinishReason ?? finishReason;
            var delta = choice?.Delta?.Content;
            if (!string.IsNullOrEmpty(delta))
            {
                text.Append(delta);
                yield return new ChatStreamUpdate(delta);
            }
        }

        yield return new ChatStreamUpdate(null, finishReason, usage ?? Estimate(request, text.ToString()));
    }

    private async Task<HttpResponseMessage> SendAsync(
        ChatRequest request,
        bool stream,
        CancellationToken cancellationToken
    )
    {
        var body = new CompletionRequest(
            options.Model,
            request.Messages.Select(m => new WireMessage(m.Role.ToString().ToLowerInvariant(), m.Content)).ToList(),
            request.MaxOutputTokens ?? options.MaxOutputTokens,
            request.Temperature ?? options.Temperature,
            stream,
            stream ? new StreamOptions(IncludeUsage: true) : null
        );

        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmProviderException($"Chat request to {http.BaseAddress} failed: {ex.Message}", null, ex);
        }
        catch (ExecutionRejectedException ex)
        {
            throw new LlmProviderException($"Chat request rejected: {ex.Message}", null, ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            if (error.Length > MaxErrorBodyLength)
            {
                error = error[..MaxErrorBodyLength] + "...";
            }

            throw new LlmProviderException(
                $"Chat provider returned {(int)response.StatusCode} {response.StatusCode}: {error}",
                response.StatusCode
            );
        }
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new LlmProviderException($"Chat stream broke off: {ex.Message}", null, ex);
        }
    }

    private static CompletionChunk ParseChunk(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<CompletionChunk>(data, Json) ?? new CompletionChunk(null, null);
        }
        catch (JsonException ex)
        {
            throw new LlmProviderException("Chat provider sent a malformed stream event.", null, ex);
        }
    }

    /// <summary>About four characters per token; only used when the provider reports no usage.</summary>
    private static ChatUsage Estimate(ChatRequest request, string output) =>
        new(request.Messages.Sum(m => m.Content.Length + 3) / 4, (output.Length + 3) / 4, Estimated: true);

    private sealed record CompletionRequest(
        string Model,
        IReadOnlyList<WireMessage> Messages,
        int MaxTokens,
        double Temperature,
        bool Stream,
        StreamOptions? StreamOptions
    );

    private sealed record StreamOptions(bool IncludeUsage);

    private sealed record WireMessage(string Role, string? Content);

    private sealed record CompletionResponse(string? Model, List<CompletionChoice>? Choices, WireUsage? Usage);

    private sealed record CompletionChoice(WireMessage? Message, string? FinishReason);

    private sealed record CompletionChunk(List<ChunkChoice>? Choices, WireUsage? Usage);

    private sealed record ChunkChoice(WireMessage? Delta, string? FinishReason);

    private sealed record WireUsage(int? PromptTokens, int? CompletionTokens);
}
