using System.Text.Json.Serialization;

namespace GuidesImporter.Application.Models;

/// <summary>The question set file, eval/questions.json.</summary>
public sealed record EvalQuestionSet(int Version, string? Notes, IReadOnlyList<EvalQuestion> Questions);

/// <summary>One evaluation question and where its answer is.</summary>
/// <param name="Article">Title of the article that answers it; null for an unanswerable question.</param>
/// <param name="Section">Top-level section heading that holds the answer, when known.</param>
public sealed record EvalQuestion(
    string Id,
    string Question,
    string? Article,
    string? Section,
    string Difficulty,
    bool Answerable
);

/// <summary>A search result as the evaluation sees it.</summary>
public sealed record EvalHit(long ChunkId, string ArticleTitle, string Section);

/// <summary>An answer as the evaluation sees it.</summary>
public sealed record EvalAnswer(
    string Answer,
    bool Answered,
    IReadOnlyList<long> CitedChunkIds,
    int InputTokens,
    int OutputTokens,
    long TotalMilliseconds
);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JudgeVerdict
{
    Supported,
    Unsupported,
    Unclear,
}

/// <summary>What happened to one question in one run.</summary>
public sealed class EvalQuestionResult
{
    public required EvalQuestion Question { get; init; }

    /// <summary>Per mode: 1-based rank of the first chunk from the right article, or null if not in the top K.</summary>
    public Dictionary<string, int?> ArticleRank { get; init; } = [];

    /// <summary>Per mode: 1-based rank of the first chunk from the right article and section.</summary>
    public Dictionary<string, int?> SectionRank { get; init; } = [];

    public Dictionary<string, long> SearchMilliseconds { get; init; } = [];

    public EvalAnswer? Answer { get; set; }

    public JudgeVerdict? Verdict { get; set; }

    public string? VerdictReason { get; set; }

    public string? Error { get; set; }
}

/// <summary>One judgement for a person to check, in eval/judgements-to-verify.json.</summary>
public sealed class JudgementToVerify
{
    public required string Id { get; init; }

    public required string Question { get; init; }

    public required string Answer { get; init; }

    public required IReadOnlyList<string> Sources { get; init; }

    public required JudgeVerdict JudgeVerdict { get; init; }

    public string? JudgeReason { get; init; }

    /// <summary>Filled in by hand: "Supported" or "Unsupported".</summary>
    public JudgeVerdict? HumanVerdict { get; set; }
}
