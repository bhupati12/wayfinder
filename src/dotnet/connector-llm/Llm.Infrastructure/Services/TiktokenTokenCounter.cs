using Llm.Core.Interfaces;
using Microsoft.ML.Tokenizers;

namespace Llm.Infrastructure.Services;

/// <summary>
/// Token counts with the cl100k_base encoding (OpenAI text-embedding-3 models). Other providers
/// tokenize differently, but counts stay within a few percent for English prose, which is
/// accurate enough for sizing chunks. Thread-safe; register as a singleton.
/// </summary>
public sealed class TiktokenTokenCounter : ITokenCounter
{
    private const string Encoding = "cl100k_base";

    private readonly Tokenizer tokenizer = TiktokenTokenizer.CreateForEncoding(Encoding);

    public string Name => Encoding;

    public int Count(string text) => text.Length == 0 ? 0 : tokenizer.CountTokens(text);
}
