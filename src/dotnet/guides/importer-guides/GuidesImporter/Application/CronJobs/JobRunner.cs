namespace GuidesImporter.Application.CronJobs;

/// <summary>
/// Resolves a job by name and runs it. Returns a process exit code:
/// 0 = success, 1 = job failed, 2 = unknown job name.
/// </summary>
public class JobRunner
{
    private readonly IEnumerable<ICronJob> jobs;
    private readonly ILogger<JobRunner> logger;

    public JobRunner(IEnumerable<ICronJob> jobs, ILogger<JobRunner> logger)
    {
        this.jobs = jobs;
        this.logger = logger;
    }

    public async Task<int> RunAsync(string jobName, CancellationToken cancellationToken)
    {
        var job = jobs.FirstOrDefault(j => string.Equals(j.Name, jobName, StringComparison.OrdinalIgnoreCase));

        if (job is null)
        {
            logger.LogError(
                "Unknown job {JobName}. Known jobs: {KnownJobs}",
                jobName,
                string.Join(", ", jobs.Select(j => j.Name))
            );
            return 2;
        }

        try
        {
            logger.LogInformation("Job {JobName} started", job.Name);
            await job.RunAsync(cancellationToken);
            logger.LogInformation("Job {JobName} finished", job.Name);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobName} failed", job.Name);
            return 1;
        }
    }
}
