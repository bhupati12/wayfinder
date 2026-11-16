using GuidesImporter.Application.CronJobs;

namespace GuidesImporter;

/// <summary>
/// One binary, two ways to run it:
///   dotnet GuidesImporter.dll              -> hosts the HTTP API (manual triggers, health checks)
///   dotnet GuidesImporter.dll jobName      -> runs one job to completion and exits (how a CronJob calls it)
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        Startup.ConfigureServices(builder);

        var app = builder.Build();

        var jobName = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (jobName is not null)
        {
            using var scope = app.Services.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<JobRunner>();
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            return await runner.RunAsync(jobName, cts.Token);
        }

        Startup.Configure(app);
        await app.RunAsync();
        return 0;
    }
}
