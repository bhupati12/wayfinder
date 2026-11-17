using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SearchService.Application.Filters;

/// <summary>
/// Switches the request timeout off once an action has produced its result.
/// </summary>
/// <remarks>
/// The timeout cancels <c>HttpContext.RequestAborted</c>, and the JSON output formatter writes
/// with that token. If the timeout fires while the body is being written, the formatter stops
/// silently and the response ends as a 200 with half a JSON document: the evaluation run hit
/// this twice when a search finished just under the limit. The work the timeout guards (the
/// database and the model) is done by then, so the result is always written out in full.
/// </remarks>
public sealed class DisableRequestTimeoutOnResultFilter : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        context.HttpContext.Features.Get<IHttpRequestTimeoutFeature>()?.DisableTimeout();
        return next();
    }
}
