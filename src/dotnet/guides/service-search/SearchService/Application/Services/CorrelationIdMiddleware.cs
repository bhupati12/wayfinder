using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SearchService.Application.Services;

/// <summary>
/// Gives every request a correlation id: the caller's X-Correlation-Id when it looks safe,
/// otherwise the trace id. It is echoed in the response, added to every log line of the request
/// (as a scope), tagged on the trace, and put in problem-details responses.
/// </summary>
public sealed partial class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate next;
    private readonly ILogger<CorrelationIdMiddleware> logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var id = SafeIdRegex().IsMatch(incoming)
            ? incoming
            : Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        context.Items[HeaderName] = id;
        context.Response.Headers[HeaderName] = id;
        Activity.Current?.SetTag("correlation.id", id);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(context);
        }
    }

    public static string? Get(HttpContext context) => context.Items[HeaderName] as string;

    [GeneratedRegex(@"^[A-Za-z0-9\-_.]{8,64}$")]
    private static partial Regex SafeIdRegex();
}
