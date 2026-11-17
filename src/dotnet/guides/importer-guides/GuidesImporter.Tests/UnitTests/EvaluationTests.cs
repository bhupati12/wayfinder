using System.Text.Json;
using GuidesImporter.Application.Helpers;
using GuidesImporter.Application.Models;
using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services;
using GuidesImporter.Application.Services.Interfaces;
using GuidesImporter.Database.Services.Interfaces;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace GuidesImporter.Tests.UnitTests;

public class RetrievalMetricsTests
{
    private static readonly EvalHit[] Hits =
    [
        new(1, "Kyoto", "See"),
        new(2, "Osaka", "Eat"),
        new(3, "Osaka", "Get in > By plane"),
        new(4, "Nara", "See"),
    ];

    [Theory]
    [InlineData("Osaka", null, 2, 2)]
    [InlineData("Osaka", "Get in", 2, 3)]
    [InlineData("osaka", "get in", 2, 3)]
    [InlineData("Nara", "Eat", 4, null)]
    [InlineData("Tokyo", "See", null, null)]
    public void Finds_the_first_relevant_rank(string article, string? section, int? articleRank, int? sectionRank)
    {
        Assert.Equal(articleRank, RetrievalMetrics.FirstArticleRank(Hits, article));
        Assert.Equal(sectionRank, RetrievalMetrics.FirstSectionRank(Hits, article, section));
    }

    [Fact]
    public void Section_match_includes_sub_sections_only()
    {
        Assert.True(RetrievalMetrics.InSection("Get in > By plane", "Get in"));
        Assert.True(RetrievalMetrics.InSection("Get in", "Get in"));
        Assert.False(RetrievalMetrics.InSection("Get into the city", "Get in"));
    }

    [Fact]
    public void Recall_and_mean_reciprocal_rank()
    {
        int?[] ranks = [1, 2, 6, null];

        Assert.Equal(0.5, RetrievalMetrics.RecallAt(ranks, 5));
        Assert.Equal(0.75, RetrievalMetrics.RecallAt(ranks, 10));
        Assert.Equal((1 + 0.5 + 1.0 / 6) / 4, RetrievalMetrics.MeanReciprocalRank(ranks), 10);
        Assert.Equal(0, RetrievalMetrics.RecallAt([], 5));
    }

    [Fact]
    public void Median_of_odd_and_even_counts()
    {
        Assert.Equal(3, RetrievalMetrics.Median([5, 1, 3]));
        Assert.Equal(3, RetrievalMetrics.Median([4, 1, 2, 9]));
        Assert.Equal(0, RetrievalMetrics.Median([]));
    }
}

public class LlmAnswerJudgeTests
{
    [Theory]
    [InlineData(
        "VERDICT: SUPPORTED\nREASON: All claims are in source 1.",
        JudgeVerdict.Supported,
        "All claims are in source 1."
    )]
    [InlineData(
        "VERDICT: UNSUPPORTED\nREASON: The price is not in the sources.",
        JudgeVerdict.Unsupported,
        "The price is not in the sources."
    )]
    [InlineData("**VERDICT:** UNSUPPORTED", JudgeVerdict.Unsupported, null)]
    [InlineData("verdict: supported", JudgeVerdict.Supported, null)]
    [InlineData("I think it is fine.", JudgeVerdict.Unclear, null)]
    public void Parses_the_judge_reply(string reply, JudgeVerdict verdict, string? reason)
    {
        Assert.Equal((verdict, reason), LlmAnswerJudge.Parse(reply));
    }
}

public class EvaluationServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "wayfinder-eval-" + Guid.NewGuid().ToString("N"));

    public EvaluationServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "eval"));
        var set = new EvalQuestionSet(
            1,
            null,
            [
                new("q1", "Where to eat ramen?", "Osaka", "Eat", "easy", true),
                new("q2", "How to get to Nara?", "Nara", "Get in", "hard", true),
                new("u1", "Capital of Kenya?", null, null, "unanswerable", false),
            ]
        );
        File.WriteAllText(
            Path.Combine(root, "eval", "questions.json"),
            JsonSerializer.Serialize(set, EvaluationService.Json)
        );
    }

    [Fact]
    public async Task Measures_retrieval_and_answers_and_writes_the_report()
    {
        var api = new FakeApi();
        var service = new EvaluationService(
            api,
            new FakeJudge(),
            new FakeChunks(),
            MsOptions.Create(new EvalOptions { RunLabel = "test run", Notes = "A unit test." }),
            MsOptions.Create(new IngestionOptions()),
            new FakeEnvironment(root),
            TimeProvider.System,
            NullLogger<EvaluationService>.Instance
        );

        var results = await service.RunAsync(CancellationToken.None);

        // q1: Osaka/Eat is first in hybrid; q2: Nara is found, but only in the wrong section.
        var q1 = results.Single(r => r.Question.Id == "q1");
        Assert.Equal(1, q1.ArticleRank["hybrid"]);
        Assert.Equal(1, q1.SectionRank["hybrid"]);
        Assert.Null(q1.ArticleRank["keyword"]);
        var q2 = results.Single(r => r.Question.Id == "q2");
        Assert.Equal(2, q2.ArticleRank["vector"]);
        Assert.Null(q2.SectionRank["vector"]);

        Assert.Equal(JudgeVerdict.Supported, q1.Verdict);
        Assert.Null(results.Single(r => r.Question.Id == "u1").Verdict); // refused, so not judged

        var report = File.ReadAllText(Path.Combine(root, "docs", "EVALUATION.md"));
        Assert.StartsWith("# Evaluation", report);
        Assert.Contains("## test run", report);
        Assert.Contains("A unit test.", report);
        Assert.Contains("| hybrid | 100% | 100% | 50% | 50% |", report);
        Assert.Contains("| keyword | 50% | 50% |", report);
        Assert.Contains("| Unanswerable questions correctly refused | 1 / 1 (100%) |", report);
        Assert.Contains("| Judged supported by their sources (LLM judge `fake-judge`) | 2 / 2 (100%) |", report);

        Assert.Single(Directory.GetFiles(Path.Combine(root, "eval", "results"), "*-test-run.json"));
        var toVerify = JsonSerializer.Deserialize<List<JudgementToVerify>>(
            File.ReadAllText(Path.Combine(root, "eval", "judgements-to-verify.json")),
            EvaluationService.Json
        )!;
        Assert.Equal(2, toVerify.Count);
        Assert.All(toVerify, j => Assert.Null(j.HumanVerdict));
        Assert.Contains("Osaka — Eat", toVerify.Single(j => j.Id == "q1").Sources[0]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private sealed class FakeApi : IEvalApiClient
    {
        public Task<IReadOnlyList<EvalHit>> SearchAsync(
            string query,
            string mode,
            int k,
            CancellationToken cancellationToken
        )
        {
            IReadOnlyList<EvalHit> hits = (query, mode) switch
            {
                ("Where to eat ramen?", "keyword") => [new(9, "Tokyo", "Eat")],
                ("Where to eat ramen?", _) => [new(1, "Osaka", "Eat"), new(9, "Tokyo", "Eat")],
                ("How to get to Nara?", _) => [new(9, "Tokyo", "Eat"), new(5, "Nara", "See")],
                _ => [new(9, "Tokyo", "Eat")],
            };
            return Task.FromResult(hits);
        }

        public Task<EvalAnswer> AskAsync(string question, CancellationToken cancellationToken) =>
            Task.FromResult(
                question.StartsWith("Capital", StringComparison.Ordinal)
                    ? new EvalAnswer("I don't know based on the available guides.", false, [], 50, 10, 100)
                    : new EvalAnswer("Try Dotonbori [1].", true, [1], 400, 20, 2000)
            );
    }

    private sealed class FakeJudge : IAnswerJudge
    {
        public string Model => "fake-judge";

        public Task<(JudgeVerdict Verdict, string? Reason)> JudgeAsync(
            string question,
            string answer,
            IReadOnlyList<string> sources,
            CancellationToken cancellationToken
        ) => Task.FromResult<(JudgeVerdict, string?)>((JudgeVerdict.Supported, "fine"));
    }

    private sealed class FakeChunks : IChunkTextRepository
    {
        public Task<IReadOnlyDictionary<long, string>> GetTextsAsync(
            IReadOnlyCollection<long> chunkIds,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyDictionary<long, string>>(
                chunkIds.ToDictionary(id => id, id => $"Osaka — Eat\n\nChunk {id} text.")
            );

        public Task<(int Articles, int Chunks, string? EmbeddingModel)> DescribeAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult<(int, int, string?)>((3, 10, "fake-embed"));
    }

    private sealed class FakeEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "GuidesImporter.Tests";

        public string ContentRootPath { get; set; } = root;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
