using Llm.Core.Models;

namespace Llm.Core.Interfaces;

/// <summary>Generates text from a chat conversation.</summary>
public interface IChatClient
{
    /// <summary>Model that answers, reported in response metadata.</summary>
    string Model { get; }

    /// <exception cref="Exceptions.LlmProviderException">The provider failed after retries.</exception>
    Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Streams the completion. The last update has <see cref="ChatStreamUpdate.IsFinal"/> set and
    /// carries usage. Retries only happen before the first token: once text has been yielded, a
    /// failure surfaces as an exception.
    /// </summary>
    /// <exception cref="Exceptions.LlmProviderException">The provider failed.</exception>
    IAsyncEnumerable<ChatStreamUpdate> StreamAsync(ChatRequest request, CancellationToken cancellationToken);
}
