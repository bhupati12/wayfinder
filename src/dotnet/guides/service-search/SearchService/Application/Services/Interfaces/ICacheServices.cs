using DomainModels.Guides.Models;

namespace SearchService.Application.Services.Interfaces;

/// <summary>
/// Cache for query embeddings and full answers. A cache failure (Redis down) is logged and
/// treated as a miss, so it slows requests down instead of failing them.
/// </summary>
public interface IResponseCache
{
    Task<float[]?> GetQueryEmbeddingAsync(string model, string query, CancellationToken cancellationToken);

    Task SetQueryEmbeddingAsync(string model, string query, float[] vector, CancellationToken cancellationToken);

    Task<AskResponse?> GetAnswerAsync(string key, CancellationToken cancellationToken);

    Task SetAnswerAsync(string key, AskResponse response, CancellationToken cancellationToken);
}

/// <summary>Daily usage of the chat model, for the cost cap.</summary>
public interface IUsageTracker
{
    /// <summary>Throws <see cref="CostCapExceededException"/> when today's tokens or cost have reached the cap.</summary>
    Task EnsureWithinCapAsync(CancellationToken cancellationToken);

    Task RecordAsync(long tokens, decimal costUsd, CancellationToken cancellationToken);
}

/// <summary>The daily cost cap is reached; /api/ask is off until the next UTC day.</summary>
public sealed class CostCapExceededException : Exception
{
    public CostCapExceededException(string message)
        : base(message) { }
}
