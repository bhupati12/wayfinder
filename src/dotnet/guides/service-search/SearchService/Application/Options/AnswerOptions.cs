using System.ComponentModel.DataAnnotations;

namespace SearchService.Application.Options;

/// <summary>Settings for /api/ask, bound from the "Answer" section.</summary>
public class AnswerOptions
{
    public const string SectionName = "Answer";

    /// <summary>Chunks retrieved (hybrid) before they are trimmed to the token budget.</summary>
    [Range(1, 20)]
    public int DefaultK { get; set; } = 6;

    /// <summary>
    /// Tokens kept free on top of the counted prompt. Prompt tokens are counted with cl100k_base,
    /// which differs from the chat model's own tokenizer, so the margin absorbs the difference.
    /// </summary>
    [Range(0, 100_000)]
    public int SafetyMarginTokens { get; set; } = 400;
}
