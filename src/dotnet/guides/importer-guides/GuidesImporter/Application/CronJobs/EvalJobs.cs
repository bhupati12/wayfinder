using System.Globalization;
using System.Text.Json;
using GuidesImporter.Application.Models;
using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services;
using Microsoft.Extensions.Options;

namespace GuidesImporter.Application.CronJobs;

/// <summary>Runs eval/questions.json through the SearchService and appends the results to docs/EVALUATION.md.</summary>
public sealed class RunEvalJob : ICronJob
{
    private readonly EvaluationService evaluation;

    public RunEvalJob(EvaluationService evaluation)
    {
        this.evaluation = evaluation;
    }

    public string Name => "runEval";

    public Task RunAsync(CancellationToken cancellationToken) => evaluation.RunAsync(cancellationToken);
}

/// <summary>
/// Compares the LLM judge with the hand check in eval/judgements-to-verify.json and appends the
/// agreement to docs/EVALUATION.md. Fill in "humanVerdict" for each item first.
/// </summary>
public sealed class ScoreJudgeAgreementJob : ICronJob
{
    private readonly EvalOptions options;
    private readonly IHostEnvironment environment;
    private readonly TimeProvider clock;
    private readonly ILogger<ScoreJudgeAgreementJob> logger;

    public ScoreJudgeAgreementJob(
        IOptions<EvalOptions> options,
        IHostEnvironment environment,
        TimeProvider clock,
        ILogger<ScoreJudgeAgreementJob> logger
    )
    {
        this.options = options.Value;
        this.environment = environment;
        this.clock = clock;
        this.logger = logger;
    }

    public string Name => "scoreJudgeAgreement";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(options.JudgementsToVerifyPath, environment.ContentRootPath);
        await using var stream = File.OpenRead(path);
        var items = await JsonSerializer.DeserializeAsync<List<JudgementToVerify>>(
            stream,
            EvaluationService.Json,
            cancellationToken
        ) ?? [];

        var checkedItems = items
            .Where(i => i.HumanVerdict is JudgeVerdict.Supported or JudgeVerdict.Unsupported)
            .ToList();
        if (checkedItems.Count == 0)
        {
            throw new InvalidOperationException($"No item in {path} has a humanVerdict yet.");
        }

        var agree = checkedItems.Count(i => i.HumanVerdict == i.JudgeVerdict);
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"### Judge agreement — {clock.GetUtcNow():yyyy-MM-dd}\n\nThe LLM judge agreed with the hand check on {agree} of {checkedItems.Count} answers ({100.0 * agree / checkedItems.Count:F0}%). "
                + $"Judge said supported, human said unsupported: {checkedItems.Count(i => i.JudgeVerdict == JudgeVerdict.Supported && i.HumanVerdict == JudgeVerdict.Unsupported)}; "
                + $"judge said unsupported, human said supported: {checkedItems.Count(i => i.JudgeVerdict != JudgeVerdict.Supported && i.HumanVerdict == JudgeVerdict.Supported)}.\n\n"
        );

        await File.AppendAllTextAsync(
            Path.GetFullPath(options.ReportPath, environment.ContentRootPath),
            line,
            cancellationToken
        );
        logger.LogInformation("Judge agreement {Agree}/{Total}", agree, checkedItems.Count);
    }
}
