using System.Globalization;
using System.Text;
using GuidesImporter.Application.Helpers;
using GuidesImporter.Application.Models;

namespace GuidesImporter.Application.Services;

/// <summary>What a run measured, plus the settings needed to reproduce it.</summary>
public sealed record EvaluationRunInfo(
    string Label,
    string? Notes,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int Articles,
    int Chunks,
    string? EmbeddingModel,
    string ChunkSettings,
    int MaxK,
    string? JudgeModel,
    string ResultsFile
);

/// <summary>Turns per-question results into the markdown section appended to docs/EVALUATION.md.</summary>
public static class EvaluationReport
{
    public const string Header = """
        # Evaluation

        Measured with the `runEval` job against the running SearchService. Each run below is appended by
        the job; rerun it with the same settings to reproduce a row. Raw per-question results are in
        `eval/results/`.

        - **Recall@K**: share of answerable questions whose answer appears in the top K search results.
          "Article" counts any chunk of the right article; "section" needs the right section too.
        - **MRR@10**: mean reciprocal rank of the first correct result (1 for rank 1, 0.5 for rank 2, 0
          when not in the top 10).
        - **Supported**: share of answered questions whose answer an LLM judge found supported by the
          cited sources. LLM judges make mistakes; the agreement with a hand check is reported
          separately once `eval/judgements-to-verify.json` has been filled in.


        """;

    public static string Build(
        EvaluationRunInfo info,
        IReadOnlyList<EvalQuestionResult> results,
        IReadOnlyList<string> modes,
        int maxK
    )
    {
        var c = CultureInfo.InvariantCulture;
        var answerable = results.Where(r => r.Question.Answerable).ToList();
        var unanswerable = results.Where(r => !r.Question.Answerable).ToList();

        var md = new StringBuilder();
        md.Append(c, $"## {info.Label} — {info.StartedAt:yyyy-MM-dd HH:mm} UTC\n\n");
        if (!string.IsNullOrWhiteSpace(info.Notes))
        {
            md.Append(info.Notes.Trim()).Append("\n\n");
        }

        md.Append(
                c,
                $"Settings: {info.Articles:N0} articles, {info.Chunks:N0} chunks ({info.ChunkSettings}); embedding model `{info.EmbeddingModel ?? "none"}`; "
            )
            .Append(
                c,
                $"{answerable.Count} answerable and {unanswerable.Count} unanswerable questions; top {maxK} results per search; run took {info.Duration:hh\\:mm\\:ss}. "
            )
            .Append(c, $"Raw results: `{info.ResultsFile}`.\n\n");

        md.Append(
            c,
            $"| Mode | Recall@5 article | Recall@{maxK} article | Recall@5 section | Recall@{maxK} section | MRR@{maxK} article | Median search ms |\n"
        );
        md.Append("|---|---|---|---|---|---|---|\n");
        foreach (var mode in modes)
        {
            var article = answerable.Select(r => r.ArticleRank.GetValueOrDefault(mode)).ToList();
            var section = answerable.Select(r => r.SectionRank.GetValueOrDefault(mode)).ToList();
            var latency = results
                .Where(r => r.SearchMilliseconds.ContainsKey(mode))
                .Select(r => r.SearchMilliseconds[mode]);
            md.Append(
                    c,
                    $"| {mode} | {Pct(RetrievalMetrics.RecallAt(article, 5))} | {Pct(RetrievalMetrics.RecallAt(article, maxK))} | "
                )
                .Append(
                    c,
                    $"{Pct(RetrievalMetrics.RecallAt(section, 5))} | {Pct(RetrievalMetrics.RecallAt(section, maxK))} | "
                )
                .Append(
                    c,
                    $"{RetrievalMetrics.MeanReciprocalRank(article):F3} | {RetrievalMetrics.Median(latency)} |\n"
                );
        }

        md.Append('\n');
        md.Append(c, $"Recall@{maxK} (article) by difficulty:");
        foreach (var group in answerable.GroupBy(r => r.Question.Difficulty).OrderBy(g => g.Key))
        {
            md.Append(c, $" {group.Key} ({group.Count()}):");
            md.Append(
                string.Join(
                    ",",
                    modes.Select(m =>
                        $" {m} {Pct(RetrievalMetrics.RecallAt(group.Select(r => r.ArticleRank.GetValueOrDefault(m)), maxK))}"
                    )
                )
            );
            md.Append(';');
        }

        md.Length--;
        md.Append(".\n\n");

        var asked = results.Where(r => r.Answer is not null).ToList();
        if (asked.Count > 0)
        {
            var answeredAnswerable = asked.Where(r => r.Question.Answerable && r.Answer!.Answered).ToList();
            var falseRefusals = asked.Count(r => r.Question.Answerable && !r.Answer!.Answered);
            var askedAnswerable = asked.Count(r => r.Question.Answerable);
            var askedUnanswerable = asked.Count(r => !r.Question.Answerable);
            var correctRefusals = asked.Count(r => !r.Question.Answerable && !r.Answer!.Answered);
            var judged = asked.Where(r => r.Verdict is JudgeVerdict.Supported or JudgeVerdict.Unsupported).ToList();
            var supported = judged.Count(r => r.Verdict == JudgeVerdict.Supported);
            var withCitation = answeredAnswerable.Count(r => r.Answer!.CitedChunkIds.Count > 0);

            md.Append("| Answers | Value |\n|---|---|\n");
            md.Append(
                c,
                $"| Answerable questions answered | {answeredAnswerable.Count} / {askedAnswerable} ({Pct(Ratio(answeredAnswerable.Count, askedAnswerable))}) |\n"
            );
            md.Append(
                c,
                $"| Answerable questions refused (\"I don't know\") | {falseRefusals} / {askedAnswerable} |\n"
            );
            md.Append(
                c,
                $"| Unanswerable questions correctly refused | {correctRefusals} / {askedUnanswerable} ({Pct(Ratio(correctRefusals, askedUnanswerable))}) |\n"
            );
            md.Append(c, $"| Answers with at least one citation | {withCitation} / {answeredAnswerable.Count} |\n");
            md.Append(
                c,
                $"| Judged supported by their sources (LLM judge `{info.JudgeModel ?? "none"}`) | {supported} / {judged.Count} ({Pct(Ratio(supported, judged.Count))}) |\n"
            );
            md.Append(c, $"| Judge verdict unclear | {asked.Count(r => r.Verdict == JudgeVerdict.Unclear)} |\n");
            md.Append(
                c,
                $"| Median answer time | {RetrievalMetrics.Median(asked.Select(r => r.Answer!.TotalMilliseconds)):N0} ms |\n"
            );
            md.Append(
                c,
                $"| Mean tokens per answer (in / out) | {asked.Average(r => r.Answer!.InputTokens):F0} / {asked.Average(r => r.Answer!.OutputTokens):F0} |\n"
            );
            md.Append(
                "| Judge agreement with hand check | pending: fill in `eval/judgements-to-verify.json`, then run `scoreJudgeAgreement` |\n\n"
            );
        }

        var errors = results.Count(r => r.Error is not null);
        if (errors > 0)
        {
            md.Append(
                c,
                $"**{errors} questions hit an error and count as misses above; see the raw results.** Rerun before quoting these numbers.\n\n"
            );
        }

        return md.ToString();
    }

    private static double Ratio(int part, int whole) => whole == 0 ? 0 : part / (double)whole;

    private static string Pct(double value) => (value * 100).ToString("F0", CultureInfo.InvariantCulture) + "%";
}
