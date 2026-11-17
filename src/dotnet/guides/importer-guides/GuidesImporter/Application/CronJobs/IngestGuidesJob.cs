using GuidesImporter.Application.Services.Interfaces;

namespace GuidesImporter.Application.CronJobs;

/// <summary>Loads Wikivoyage articles from the dump into guide_articles and guide_chunks.</summary>
public sealed class IngestGuidesJob : ICronJob
{
    private readonly IIngestionService ingestion;

    public IngestGuidesJob(IIngestionService ingestion)
    {
        this.ingestion = ingestion;
    }

    public string Name => "ingestGuides";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var summary = await ingestion.IngestAsync(cancellationToken);
        if (summary.ArticlesFailed > 0)
        {
            // The run row is already marked Succeeded with the failure count; a non-zero exit code
            // makes the Kubernetes CronJob show it too.
            throw new InvalidOperationException($"{summary.ArticlesFailed} articles failed to ingest; see the log.");
        }
    }
}
