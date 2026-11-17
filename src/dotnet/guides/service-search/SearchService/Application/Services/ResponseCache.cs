using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DomainModels.Guides.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using SearchService.Application.Options;
using SearchService.Application.Services.Interfaces;

namespace SearchService.Application.Services;

/// <summary><see cref="IResponseCache"/> over <see cref="IDistributedCache"/> (Redis or memory).</summary>
public sealed partial class ResponseCache : IResponseCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IDistributedCache cache;
    private readonly CachingOptions options;
    private readonly ILogger<ResponseCache> logger;

    public ResponseCache(IDistributedCache cache, IOptions<CachingOptions> options, ILogger<ResponseCache> logger)
    {
        this.cache = cache;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <summary>
    /// Lower case, single spaces, no trailing punctuation: "Where is Akita?" and
    /// "where is  akita" share a cache entry.
    /// </summary>
    public static string Normalize(string text) =>
        SpacesRegex().Replace(text.Trim().ToLowerInvariant(), " ").TrimEnd('?', '.', '!', ' ');

    /// <summary>A key that changes whenever anything that shapes the answer changes.</summary>
    public static string AnswerKey(
        string question,
        int k,
        string promptVersion,
        string chatModel,
        string embeddingModel
    ) => "wayfinder:answer:" + Hash($"{Normalize(question)}|{k}|{promptVersion}|{chatModel}|{embeddingModel}");

    public async Task<float[]?> GetQueryEmbeddingAsync(string model, string query, CancellationToken cancellationToken)
    {
        var bytes = await GetAsync("embedding", EmbeddingKey(model, query), cancellationToken);
        if (bytes is null || bytes.Length % 4 != 0)
        {
            return null;
        }

        var vector = new float[bytes.Length / 4];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
        }

        return vector;
    }

    public Task SetQueryEmbeddingAsync(string model, string query, float[] vector, CancellationToken cancellationToken)
    {
        var bytes = new byte[vector.Length * 4];
        for (var i = 0; i < vector.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), vector[i]);
        }

        return SetAsync(EmbeddingKey(model, query), bytes, options.QueryEmbeddingTtl, cancellationToken);
    }

    public async Task<AskResponse?> GetAnswerAsync(string key, CancellationToken cancellationToken)
    {
        var bytes = await GetAsync("answer", key, cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<AskResponse>(bytes, Json);
    }

    public Task SetAnswerAsync(string key, AskResponse response, CancellationToken cancellationToken) =>
        SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(response, Json), options.AnswerTtl, cancellationToken);

    private static string EmbeddingKey(string model, string query) =>
        "wayfinder:embedding:" + Hash($"{model}|{Normalize(query)}");

    private async Task<byte[]?> GetAsync(string kind, string key, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return null;
        }

        try
        {
            var bytes = await cache.GetAsync(key, cancellationToken);
            Telemetry.CacheLookup(kind, bytes is null ? "miss" : "hit");
            return bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Telemetry.CacheLookup(kind, "error");
            logger.LogWarning(ex, "Cache read failed; continuing without the cache");
            return null;
        }
    }

    private async Task SetAsync(string key, byte[] value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        try
        {
            await cache.SetAsync(
                key,
                value,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache write failed; continuing without the cache");
        }
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
