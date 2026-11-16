using System.Diagnostics;
using System.Text.RegularExpressions;
using GuidesDbEntities.Models;
using GuidesImporter.Application.Helpers;
using GuidesImporter.Application.Models;
using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services.Interfaces;
using GuidesImporter.Database.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace GuidesImporter.Application.Services;

/// <summary>
/// Read, clean, chunk, upsert. Idempotent: an article is rewritten only when its raw text hash or
/// processing signature changed, and its chunks are replaced inside the same transaction as the
/// article row, so a crash never leaves an article with half of its chunks.
/// </summary>
public sealed partial class IngestionService : IIngestionService
{
    private readonly IDumpReader dumpReader;
    private readonly IRegionResolver regionResolver;
    private readonly IArticleProcessor processor;
    private readonly IGuideArticleRepository articles;
    private readonly IIngestionRunRepository runs;
    private readonly IngestionOptions options;
    private readonly IHostEnvironment environment;
    private readonly TimeProvider clock;
    private readonly ILogger<IngestionService> logger;

    public IngestionService(
        IDumpReader dumpReader,
        IRegionResolver regionResolver,
        IArticleProcessor processor,
        IGuideArticleRepository articles,
        IIngestionRunRepository runs,
        IOptions<IngestionOptions> options,
        IHostEnvironment environment,
        TimeProvider clock,
        ILogger<IngestionService> logger
    )
    {
        this.dumpReader = dumpReader;
        this.regionResolver = regionResolver;
        this.processor = processor;
        this.articles = articles;
        this.runs = runs;
        this.options = options.Value;
        this.environment = environment;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<IngestionSummary> IngestAsync(CancellationToken cancellationToken)
    {
        var dumpPath = Path.GetFullPath(options.DumpPath, environment.ContentRootPath);
        if (!File.Exists(dumpPath))
        {
            throw new FileNotFoundException($"Dump file not found: {dumpPath}", dumpPath);
        }

        var runId = await runs.StartAsync(dumpPath, clock.GetUtcNow(), cancellationToken);
        var summary = new IngestionSummary { RunId = runId };
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Ingestion run {RunId} reading {DumpPath} (regions {Regions}, limit {Limit}, signature {Signature})",
            runId,
            dumpPath,
            options.Regions.Length == 0 ? "all" : string.Join(", ", options.Regions),
            options.ArticleLimit,
            processor.ProcessingSignature[..12]
        );

        try
        {
            await RunAsync(dumpPath, summary, stopwatch, cancellationToken);
            await runs.FinishAsync(summary, IngestionRunStatus.Succeeded, clock.GetUtcNow(), null, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await runs.FinishAsync(summary, IngestionRunStatus.Cancelled, clock.GetUtcNow(), null, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await runs.FinishAsync(summary, IngestionRunStatus.Failed, clock.GetUtcNow(), ex.Message, CancellationToken.None);
            throw;
        }

        logger.LogInformation(
            "Ingestion run {RunId} finished in {Elapsed}: {Added} added, {Updated} updated, {Skipped} skipped, {Failed} failed, {Chunks} chunks written, {Ignored} pages ignored of {Read} read",
            runId,
            stopwatch.Elapsed,
            summary.ArticlesAdded,
            summary.ArticlesUpdated,
            summary.ArticlesSkipped,
            summary.ArticlesFailed,
            summary.ChunksWritten,
            summary.PagesIgnored,
            summary.PagesRead
        );
        return summary;
    }

    private async Task RunAsync(string dumpPath, IngestionSummary summary, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        IReadOnlySet<string>? region = null;
        var regions = options.Regions.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (regions.Count > 0)
        {
            region = await regionResolver.ResolveAsync(dumpPath, regions, cancellationToken);
        }

        var batch = new List<PreparedArticle>(options.BatchSize);
        var eligible = 0;

        await foreach (var page in dumpReader.ReadPagesAsync(dumpPath, cancellationToken))
        {
            summary.PagesRead++;
            if (!IsCandidate(page, region))
            {
                summary.PagesIgnored++;
                continue;
            }

            PreparedArticle prepared;
            try
            {
                prepared = processor.Prepare(page);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not process page {PageId} {Title}", page.PageId, page.Title);
                summary.ArticlesFailed++;
                continue;
            }

            if (prepared.TotalTokens < options.MinArticleTokens || prepared.Chunks.Count == 0)
            {
                summary.PagesIgnored++;
                continue;
            }

            batch.Add(prepared);
            eligible++;

            if (batch.Count >= options.BatchSize)
            {
                await FlushAsync(batch, summary, cancellationToken);
            }

            if (eligible % options.ProgressLogInterval == 0)
            {
                LogProgress(summary, stopwatch);
            }

            if (options.ArticleLimit > 0 && eligible >= options.ArticleLimit)
            {
                logger.LogInformation("Reached the article limit of {Limit}", options.ArticleLimit);
                break;
            }
        }

        await FlushAsync(batch, summary, cancellationToken);
        LogProgress(summary, stopwatch);
    }

    private static bool IsCandidate(DumpPage page, IReadOnlySet<string>? region)
    {
        if (!page.IsArticle || DisambiguationRegex().IsMatch(page.Text))
        {
            return false;
        }

        return region is null || region.Contains(WikiTitles.Normalize(page.Title));
    }

    private async Task FlushAsync(List<PreparedArticle> batch, IngestionSummary summary, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var stored = await articles.GetStatesAsync(batch.Select(a => a.PageId).ToList(), cancellationToken);
        var added = new List<PreparedArticle>();
        var updated = new List<PreparedArticle>();
        var skipped = 0;

        foreach (var article in batch)
        {
            if (!stored.TryGetValue(article.PageId, out var state))
            {
                added.Add(article);
            }
            else if (state.RawTextHash != article.RawTextHash || state.ProcessingSignature != article.ProcessingSignature)
            {
                updated.Add(article);
            }
            else
            {
                skipped++;
            }
        }

        summary.ArticlesSkipped += skipped;

        if (added.Count + updated.Count > 0)
        {
            try
            {
                await articles.SaveBatchAsync(added, updated, clock.GetUtcNow(), cancellationToken);
                Count(summary, added, updated);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad article should not sink the batch: retry one at a time to isolate it.
                logger.LogWarning(ex, "Batch write failed; retrying {Count} articles one at a time", added.Count + updated.Count);
                await SaveIndividuallyAsync(added, updated, summary, cancellationToken);
            }
        }

        batch.Clear();
        await runs.UpdateCountsAsync(summary, cancellationToken);
    }

    private async Task SaveIndividuallyAsync(
        List<PreparedArticle> added,
        List<PreparedArticle> updated,
        IngestionSummary summary,
        CancellationToken cancellationToken
    )
    {
        foreach (var (article, isNew) in added.Select(a => (a, true)).Concat(updated.Select(a => (a, false))))
        {
            try
            {
                await articles.SaveBatchAsync(isNew ? [article] : [], isNew ? [] : [article], clock.GetUtcNow(), cancellationToken);
                Count(summary, isNew ? [article] : [], isNew ? [] : [article]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not save page {PageId} {Title}", article.PageId, article.Title);
                summary.ArticlesFailed++;
            }
        }
    }

    private static void Count(IngestionSummary summary, IReadOnlyCollection<PreparedArticle> added, IReadOnlyCollection<PreparedArticle> updated)
    {
        summary.ArticlesAdded += added.Count;
        summary.ArticlesUpdated += updated.Count;
        summary.ChunksWritten += added.Sum(a => a.Chunks.Count) + updated.Sum(a => a.Chunks.Count);
    }

    private void LogProgress(IngestionSummary summary, Stopwatch stopwatch) =>
        logger.LogInformation(
            "Progress: {Read} pages read, {Added} added, {Updated} updated, {Skipped} skipped, {Failed} failed, {Elapsed:mm\\:ss} elapsed",
            summary.PagesRead,
            summary.ArticlesAdded,
            summary.ArticlesUpdated,
            summary.ArticlesSkipped,
            summary.ArticlesFailed,
            stopwatch.Elapsed
        );

    [GeneratedRegex(@"\{\{\s*(disamb|disambig|disambiguation)\s*(\||\}\})", RegexOptions.IgnoreCase)]
    private static partial Regex DisambiguationRegex();
}
