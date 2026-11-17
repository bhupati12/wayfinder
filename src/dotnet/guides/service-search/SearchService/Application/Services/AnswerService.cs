using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using DomainModels.Guides.Models;
using Llm.Core.Interfaces;
using Llm.Core.Models;
using Llm.Infrastructure.Options;
using Microsoft.Extensions.Options;
using SearchService.Application.Helpers;
using SearchService.Application.Models;
using SearchService.Application.Options;
using SearchService.Application.Services.Interfaces;

namespace SearchService.Application.Services;

/// <summary>
/// Retrieve (hybrid), trim the sources to the model's token budget, ask the model, and map its
/// citation numbers back to Wikivoyage articles.
/// </summary>
public sealed class AnswerService : IAnswerService
{
    private readonly ISearchService search;
    private readonly IChatClient chat;
    private readonly IEmbeddingClient embeddings;
    private readonly IPromptBuilder prompts;
    private readonly IResponseCache cache;
    private readonly IUsageTracker usageTracker;
    private readonly ITokenCounter tokens;
    private readonly ChatOptions chatOptions;
    private readonly AnswerOptions options;
    private readonly ILogger<AnswerService> logger;

    public AnswerService(
        ISearchService search,
        IChatClient chat,
        IEmbeddingClient embeddings,
        IPromptBuilder prompts,
        IResponseCache cache,
        IUsageTracker usage,
        ITokenCounter tokens,
        IOptions<ChatOptions> chatOptions,
        IOptions<AnswerOptions> options,
        ILogger<AnswerService> logger
    )
    {
        this.search = search;
        this.chat = chat;
        this.embeddings = embeddings;
        this.prompts = prompts;
        this.cache = cache;
        this.usageTracker = usage;
        this.tokens = tokens;
        this.chatOptions = chatOptions.Value;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<AskResponse> AskAsync(string question, int? k, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        using var activity = Telemetry.Source.StartActivity("ask");
        await usageTracker.EnsureWithinCapAsync(cancellationToken);

        var key = CacheKey(question, k);
        if (await cache.GetAnswerAsync(key, cancellationToken) is { } cached)
        {
            activity?.SetTag("cache.hit", true);
            return FromCache(cached, total);
        }

        var context = await PrepareAsync(question, k, cancellationToken);
        if (context.Sources.Count == 0)
        {
            return Finish(context, prompts.NoAnswerText, new ChatUsage(0, 0, Estimated: false), 0, total);
        }

        var generation = Stopwatch.StartNew();
        var completion = await chat.CompleteAsync(new ChatRequest(context.Messages), cancellationToken);
        var response = Finish(context, completion.Text, completion.Usage, generation.ElapsedMilliseconds, total);
        await AfterAnswerAsync(key, response, cancellationToken);
        return response;
    }

    public async IAsyncEnumerable<AnswerStreamEvent> StreamAsync(
        string question,
        int? k,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var total = Stopwatch.StartNew();
        using var activity = Telemetry.Source.StartActivity("ask.stream");
        await usageTracker.EnsureWithinCapAsync(cancellationToken);

        var key = CacheKey(question, k);
        if (await cache.GetAnswerAsync(key, cancellationToken) is { } cached)
        {
            var response = FromCache(cached, total);
            yield return new AnswerStreamEvent.Sources(response.Citations);
            yield return new AnswerStreamEvent.Token(response.Answer);
            yield return new AnswerStreamEvent.Done(response);
            yield break;
        }

        var context = await PrepareAsync(question, k, cancellationToken);
        if (context.Sources.Count == 0)
        {
            yield return new AnswerStreamEvent.Sources([]);
            yield return new AnswerStreamEvent.Done(
                Finish(context, prompts.NoAnswerText, new ChatUsage(0, 0, false), 0, total)
            );
            yield break;
        }

        var generation = Stopwatch.StartNew();
        await using var updates = chat.StreamAsync(new ChatRequest(context.Messages), cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        // Wait for the model's first update before emitting anything, so an unavailable model
        // becomes a 503 instead of a broken stream.
        var hasUpdate = await updates.MoveNextAsync();
        yield return new AnswerStreamEvent.Sources(context.Sources.Select(ToCitation).ToList());

        var text = new StringBuilder();
        ChatUsage? usage = null;
        while (hasUpdate)
        {
            var update = updates.Current;
            if (!string.IsNullOrEmpty(update.TextDelta))
            {
                text.Append(update.TextDelta);
                yield return new AnswerStreamEvent.Token(update.TextDelta);
            }

            usage = update.Usage ?? usage;
            hasUpdate = await updates.MoveNextAsync();
        }

        var final = Finish(
            context,
            text.ToString(),
            usage ?? new ChatUsage(0, 0, Estimated: true),
            generation.ElapsedMilliseconds,
            total
        );
        await AfterAnswerAsync(key, final, CancellationToken.None);
        yield return new AnswerStreamEvent.Done(final);
    }

    private string CacheKey(string question, int? k) =>
        ResponseCache.AnswerKey(question, k ?? options.DefaultK, prompts.Version, chat.Model, embeddings.Model);

    private static AskResponse FromCache(AskResponse cached, Stopwatch total) =>
        cached with
        {
            Metadata = cached.Metadata with
            {
                Cached = true,
                InputTokens = 0,
                OutputTokens = 0,
                EstimatedCostUsd = 0,
                RetrievalMilliseconds = 0,
                GenerationMilliseconds = 0,
                TotalMilliseconds = total.ElapsedMilliseconds,
            },
        };

    /// <summary>Records usage for the cost cap and metrics, and caches answers built from full retrieval.</summary>
    private async Task AfterAnswerAsync(string key, AskResponse response, CancellationToken cancellationToken)
    {
        var m = response.Metadata;
        Telemetry.LlmDuration.Record(m.GenerationMilliseconds, new KeyValuePair<string, object?>("model", m.Model));
        Telemetry.Tokens.Add(m.InputTokens, new KeyValuePair<string, object?>("direction", "input"));
        Telemetry.Tokens.Add(m.OutputTokens, new KeyValuePair<string, object?>("direction", "output"));
        await usageTracker.RecordAsync(m.InputTokens + m.OutputTokens, m.EstimatedCostUsd, cancellationToken);

        if (!m.RetrievalDegraded)
        {
            await cache.SetAnswerAsync(key, response, cancellationToken);
        }
    }

    private sealed record PreparedQuestion(
        string Question,
        IReadOnlyList<PromptSource> Sources,
        IReadOnlyList<ChatMessage> Messages,
        int Retrieved,
        bool Degraded,
        long RetrievalMilliseconds
    );

    private async Task<PreparedQuestion> PrepareAsync(string question, int? k, CancellationToken cancellationToken)
    {
        question = question.Trim();
        var retrieval = Stopwatch.StartNew();
        var results = await search.SearchAsync(question, SearchMode.Hybrid, k ?? options.DefaultK, cancellationToken);
        retrieval.Stop();
        Activity.Current?.SetTag("ask.sources_retrieved", results.Results.Count);

        // Budget: what is left of the context window after the answer, the prompt without
        // sources, and a safety margin. Sources are taken in rank order while they fit.
        var fixedPrompt = prompts.Build(question, []).Sum(m => tokens.Count(m.Content) + 4);
        var budget =
            chatOptions.ContextWindowTokens - chatOptions.MaxOutputTokens - fixedPrompt - options.SafetyMarginTokens;

        var sources = new List<PromptSource>();
        foreach (var result in results.Results)
        {
            var source = new PromptSource(
                sources.Count + 1,
                result.ChunkId,
                result.ArticleTitle,
                result.Section,
                result.SourceUrl,
                result.Text
            );
            var cost = tokens.Count(prompts.FormatSource(source)) + 2;
            if (cost > budget)
            {
                continue;
            }

            sources.Add(source);
            budget -= cost;
        }

        if (sources.Count < results.Results.Count)
        {
            logger.LogInformation(
                "Token budget kept {Used} of {Retrieved} sources",
                sources.Count,
                results.Results.Count
            );
        }

        return new PreparedQuestion(
            question,
            sources,
            prompts.Build(question, sources),
            results.Results.Count,
            results.Degraded,
            retrieval.ElapsedMilliseconds
        );
    }

    private AskResponse Finish(
        PreparedQuestion context,
        string text,
        ChatUsage usage,
        long generationMilliseconds,
        Stopwatch total
    )
    {
        var answer = text.Trim();
        var answered = !answer.StartsWith("I don't know", StringComparison.OrdinalIgnoreCase) && answer.Length > 0;
        var byNumber = context.Sources.ToDictionary(s => s.Number);
        var numbers = CitationParser.Parse(answer);

        var citations = answered
            ? numbers.Where(byNumber.ContainsKey).Select(n => ToCitation(byNumber[n])).ToList()
            : [];

        return new AskResponse
        {
            Question = context.Question,
            Answer = answered ? answer : prompts.NoAnswerText,
            Answered = answered,
            Citations = citations,
            Metadata = new AnswerMetadata
            {
                PromptVersion = prompts.Version,
                Model = chat.Model,
                SourcesRetrieved = context.Retrieved,
                SourcesUsed = context.Sources.Count,
                RetrievalDegraded = context.Degraded,
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                TokensEstimated = usage.Estimated,
                InvalidCitations = numbers.Count(n => !byNumber.ContainsKey(n)),
                RetrievalMilliseconds = context.RetrievalMilliseconds,
                GenerationMilliseconds = generationMilliseconds,
                TotalMilliseconds = total.ElapsedMilliseconds,
                EstimatedCostUsd =
                    (
                        usage.InputTokens * chatOptions.CostPerMillionInputTokens
                        + usage.OutputTokens * chatOptions.CostPerMillionOutputTokens
                    ) / 1_000_000m,
            },
        };
    }

    private static Citation ToCitation(PromptSource s) =>
        new(s.Number, s.ChunkId, s.ArticleTitle, s.Section, s.SourceUrl);
}
