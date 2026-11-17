namespace Llm.Core.Models;

/// <summary>Vectors for one embedding request, plus what it cost.</summary>
/// <param name="Vectors">One vector per input, in input order.</param>
/// <param name="InputTokens">Tokens the provider billed (or counted) for the request.</param>
/// <param name="TokensReportedByProvider">
/// False when the provider did not report usage and <paramref name="InputTokens"/> is an estimate.
/// </param>
public sealed record EmbeddingResult(IReadOnlyList<float[]> Vectors, int InputTokens, bool TokensReportedByProvider);

public enum EmbeddingInputType
{
    /// <summary>Text that is stored and searched over.</summary>
    Document,

    /// <summary>A user's search query.</summary>
    Query,
}
