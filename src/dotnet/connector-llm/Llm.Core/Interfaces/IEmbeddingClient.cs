using Llm.Core.Models;

namespace Llm.Core.Interfaces;

/// <summary>Turns text into embedding vectors.</summary>
public interface IEmbeddingClient
{
    /// <summary>Model that produces the vectors, stored next to each vector.</summary>
    string Model { get; }

    /// <summary>Length of every vector this client returns.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Embeds <paramref name="inputs"/> in one request. Vectors come back in input order.
    /// </summary>
    /// <param name="inputs">Texts to embed. Keep the list within the provider's batch limit.</param>
    /// <param name="inputType">
    /// Whether the texts are stored documents or search queries. Some models (nomic-embed-text,
    /// Voyage) embed the two differently and retrieve better when told which is which.
    /// </param>
    /// <exception cref="Exceptions.LlmProviderException">The provider failed after retries.</exception>
    Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken
    );
}
