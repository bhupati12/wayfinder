using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SearchService.Application.Services;

/// <summary>
/// Traces and metrics of the service's own work, exported through OpenTelemetry next to the
/// standard ASP.NET Core and HttpClient instruments.
/// </summary>
public static class Telemetry
{
    public const string Name = "Wayfinder.SearchService";

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    public static readonly Histogram<double> RetrievalDuration = Meter.CreateHistogram<double>(
        "wayfinder.retrieval.duration",
        "ms",
        "Time to retrieve chunks for a search or a question."
    );

    public static readonly Histogram<double> LlmDuration = Meter.CreateHistogram<double>(
        "wayfinder.llm.duration",
        "ms",
        "Time the chat model took to answer."
    );

    public static readonly Counter<long> Tokens = Meter.CreateCounter<long>(
        "wayfinder.llm.tokens",
        "{token}",
        "Chat model tokens, tagged direction=input|output."
    );

    public static readonly Counter<long> CacheLookups = Meter.CreateCounter<long>(
        "wayfinder.cache.lookups",
        "{lookup}",
        "Cache lookups, tagged cache=answer|embedding and result=hit|miss|error."
    );

    public static void CacheLookup(string cache, string result) =>
        CacheLookups.Add(
            1,
            new KeyValuePair<string, object?>("cache", cache),
            new KeyValuePair<string, object?>("result", result)
        );
}
