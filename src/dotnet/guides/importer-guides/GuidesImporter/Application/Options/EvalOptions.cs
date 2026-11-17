using System.ComponentModel.DataAnnotations;

namespace GuidesImporter.Application.Options;

/// <summary>
/// Settings for the runEval and scoreJudgeAgreement jobs, bound from the "Eval" section. Paths
/// are resolved against the content root.
/// </summary>
public class EvalOptions
{
    public const string SectionName = "Eval";

    public string QuestionsPath { get; set; } = "eval/questions.json";

    /// <summary>The running SearchService; the evaluation goes through its public API.</summary>
    [Required, Url]
    public string SearchServiceBaseUrl { get; set; } = "http://localhost:5099/";

    public string SearchPath { get; set; } = "api/v1/search";

    public string AskPath { get; set; } = "api/v1/ask";

    /// <summary>API key sent as X-Api-Key, when the service requires one.</summary>
    public string? ApiKey { get; set; }

    public string[] Modes { get; set; } = ["hybrid", "vector", "keyword"];

    /// <summary>Results requested per search; recall is reported at 5 and at this value.</summary>
    [Range(5, 50)]
    public int MaxK { get; set; } = 10;

    /// <summary>Ask every question through /api/ask and judge the answers.</summary>
    public bool EvaluateAnswers { get; set; } = true;

    /// <summary>Short name of the run in the report, for example "baseline" or "title-boost-0".</summary>
    [Required]
    public string RunLabel { get; set; } = "baseline";

    /// <summary>Free text describing what is different about this run.</summary>
    public string? Notes { get; set; }

    public string ReportPath { get; set; } = "docs/EVALUATION.md";

    /// <summary>Folder for the raw per-question results of each run (JSON).</summary>
    public string ResultsDirectory { get; set; } = "eval/results";

    /// <summary>Judgements written for a person to check, and read back by scoreJudgeAgreement.</summary>
    public string JudgementsToVerifyPath { get; set; } = "eval/judgements-to-verify.json";

    /// <summary>How many judged answers to put in the file for hand checking (the plan asks for 20 to 30).</summary>
    [Range(1, 1000)]
    public int JudgementsToVerify { get; set; } = 30;

    [Range(1, 3600)]
    public int RequestTimeoutSeconds { get; set; } = 900;
}
