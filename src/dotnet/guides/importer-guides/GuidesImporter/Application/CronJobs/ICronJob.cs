namespace GuidesImporter.Application.CronJobs;

/// <summary>
/// A unit of batch work that runs to completion and exits.
/// Name is the command-line argument that selects it.
/// </summary>
public interface ICronJob
{
    string Name { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
