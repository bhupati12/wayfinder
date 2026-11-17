namespace Llm.Core.Interfaces;

/// <summary>
/// Counts model tokens. Chunk limits are in tokens, not characters, because embedding and chat
/// models bill and truncate by tokens; English averages about four characters per token, but
/// numbers, URLs and non-Latin scripts take many more tokens per character.
/// </summary>
public interface ITokenCounter
{
    /// <summary>Name of the encoding, for example "cl100k_base".</summary>
    string Name { get; }

    int Count(string text);
}
