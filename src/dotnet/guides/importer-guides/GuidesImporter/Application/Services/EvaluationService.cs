using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuidesImporter.Application.Helpers;
using GuidesImporter.Application.Models;
using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services.Interfaces;
using GuidesImporter.Database.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace GuidesImporter.Application.Services;

/// <summary>Runs the question set through search (every mode) and ask, judges the answers, and writes the report.</summary>
public sealed class EvaluationService
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IEvalApiClient api;
    private readonly IAnswerJudge judge;
    private readonly IChunkTextRepository chunks;
    private readonly EvalOptions options;
    private readonly IngestionOptions ingestion;
    private readonly IHostEnvironment environment;
    private readonly TimeProvider clock;
    private readonly ILogger<EvaluationService> logger;

    public EvaluationService(
        IEvalApiClient api,
        IAnswerJudge judge,
        IChunkTextRepository chunks,
        IOptions<EvalOptions> options,
        IOptions<IngestionOptions> ingestion,
        IHostEnvironment environment,
        TimeProvider clock,
        ILogger<EvaluationService> logger
    )
    {
        this.api = api;
        this.judge = judge;
        this.chunks = chunks;
        this.options = options.Value;
        this.ingestion = ingestion.Value;
        this.environment = environment;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<EvalQuestionResult>> RunAsync(CancellationToken cancellationToken)
    {
        var started = clock.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var set = await LoadQuestionsAsync(cancellationToken);
        logger.LogInformation("Evaluating {Count} questions, run {Label}", set.Questions.Count, options.RunLabel);

        // Retrieval for every question first, then every answer. A small machine may hold only one
        // model at a time: interleaving them made the server swap the embedding and chat models for
        // every question (90 reloads in one run), and searches timed out waiting for the reload.
        // The answer pass reuses the query embeddings cached by the retrieval pass.
        var results = set.Questions.Select(q => new EvalQuestionResult { Question = q }).ToList();
        for (var i = 0; i < results.Count; i++)
        {
            await SearchAsync(results[i], cancellationToken);
            LogProgress("Retrieval", i + 1, results.Count);
        }

        if (options.EvaluateAnswers)
        {
            for (var i = 0; i < results.Count; i++)
            {
                await AnswerAsync(results[i], cancellationToken);
                LogProgress("Answers", i + 1, results.Count);
            }
        }

        var stamp = started.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
        var resultsFile = Path.Combine(options.ResultsDirectory, $"{stamp}-{Slug(options.RunLabel)}.json")
            .Replace('\\', '/');
        await WriteJsonAsync(Resolve(resultsFile), results, cancellationToken);
        await WriteJudgementsToVerifyAsync(results, cancellationToken);

        var (articles, chunkCount, embeddingModel) = await chunks.DescribeAsync(cancellationToken);
        var info = new EvaluationRunInfo(
            options.RunLabel,
            options.Notes,
            started,
            stopwatch.Elapsed,
            articles,
            chunkCount,
            embeddingModel,
            $"chunks {ingestion.MinChunkTokens}-{ingestion.MaxChunkTokens} tokens, overlap {ingestion.OverlapTokens}",
            options.MaxK,
            options.EvaluateAnswers ? judge.Model : null,
            // Shown relative to the report, so it is also a working link from docs/.
            Path.GetRelativePath(Path.GetDirectoryName(Resolve(options.ReportPath))!, Resolve(resultsFile))
                .Replace('\\', '/')
        );
        await AppendReportAsync(
            EvaluationReport.Build(info, results, options.EffectiveModes, options.MaxK),
            cancellationToken
        );

        logger.LogInformation(
            "Evaluation {Label} finished in {Elapsed}; report: {Report}",
            options.RunLabel,
            stopwatch.Elapsed,
            options.ReportPath
        );
        return results;
    }

    private void LogProgress(string pass, int done, int total)
    {
        if (done % 10 == 0 || done == total)
        {
            logger.LogInformation("{Pass}: {Done}/{Total} questions", pass, done, total);
        }
    }

    private async Task SearchAsync(EvalQuestionResult result, CancellationToken cancellationToken)
    {
        var question = result.Question;
        try
        {
            foreach (var mode in options.EffectiveModes)
            {
                var timer = Stopwatch.StartNew();
                var hits = await api.SearchAsync(question.Question, mode, options.MaxK, cancellationToken);
                result.SearchMilliseconds[mode] = timer.ElapsedMilliseconds;
                if (question.Article is { } article)
                {
                    result.ArticleRank[mode] = RetrievalMetrics.FirstArticleRank(hits, article);
                    result.SectionRank[mode] = RetrievalMetrics.FirstSectionRank(hits, article, question.Section);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            RecordError(result, "search", ex);
        }
    }

    private async Task AnswerAsync(EvalQuestionResult result, CancellationToken cancellationToken)
    {
        var question = result.Question;
        try
        {
            result.Answer = await api.AskAsync(question.Question, cancellationToken);
            if (result.Answer.Answered)
            {
                var texts = await chunks.GetTextsAsync(result.Answer.CitedChunkIds, cancellationToken);
                var sources = result.Answer.CitedChunkIds.Where(texts.ContainsKey).Select(id => texts[id]).ToList();
                (result.Verdict, result.VerdictReason) = await judge.JudgeAsync(
                    question.Question,
                    result.Answer.Answer,
                    sources,
                    cancellationToken
                );
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            RecordError(result, "answer", ex);
        }
    }

    private void RecordError(EvalQuestionResult result, string pass, Exception ex)
    {
        logger.LogWarning(ex, "Question {Id} failed in the {Pass} pass", result.Question.Id, pass);
        var message = $"{pass}: {ex.Message}";
        result.Error = result.Error is null ? message : $"{result.Error}; {message}";
    }

    private async Task<EvalQuestionSet> LoadQuestionsAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(Resolve(options.QuestionsPath));
        return await JsonSerializer.DeserializeAsync<EvalQuestionSet>(stream, Json, cancellationToken)
            ?? throw new InvalidOperationException("The question set is empty.");
    }

    /// <summary>Writes a spread of judged answers (supported and unsupported) for a person to check.</summary>
    private async Task WriteJudgementsToVerifyAsync(
        List<EvalQuestionResult> results,
        CancellationToken cancellationToken
    )
    {
        var judged = results.Where(r => r.Verdict is not null && r.Answer is not null).ToList();
        if (judged.Count == 0)
        {
            return;
        }

        // Alternate verdicts so the sample covers both kinds of judgement.
        var supported = judged.Where(r => r.Verdict == JudgeVerdict.Supported).ToList();
        var other = judged.Where(r => r.Verdict != JudgeVerdict.Supported).ToList();
        var sample = new List<EvalQuestionResult>();
        for (var i = 0; sample.Count < options.JudgementsToVerify && (i < supported.Count || i < other.Count); i++)
        {
            if (i < other.Count)
            {
                sample.Add(other[i]);
            }

            if (i < supported.Count && sample.Count < options.JudgementsToVerify)
            {
                sample.Add(supported[i]);
            }
        }

        var texts = await chunks.GetTextsAsync(
            sample.SelectMany(r => r.Answer!.CitedChunkIds).Distinct().ToList(),
            cancellationToken
        );
        var items = sample
            .Select(r => new JudgementToVerify
            {
                Id = r.Question.Id,
                Question = r.Question.Question,
                Answer = r.Answer!.Answer,
                Sources = r.Answer.CitedChunkIds.Where(texts.ContainsKey).Select(id => texts[id]).ToList(),
                JudgeVerdict = r.Verdict!.Value,
                JudgeReason = r.VerdictReason,
            })
            .ToList();

        await WriteJsonAsync(Resolve(options.JudgementsToVerifyPath), items, cancellationToken);
    }

    private async Task AppendReportAsync(string section, CancellationToken cancellationToken)
    {
        var path = Resolve(options.ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(path, EvaluationReport.Header, cancellationToken);
        }

        await File.AppendAllTextAsync(path, section, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, Json, cancellationToken);
    }

    private string Resolve(string path) => Path.GetFullPath(path, environment.ContentRootPath);

    private static string Slug(string label) =>
        string.Join(
            '-',
            new string(label.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray()).Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
            )
        );
}
