using Llm.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SearchService.Application.Services;

/// <summary>Turns a failing model provider into a 503 problem-details response instead of a 500.</summary>
public sealed class LlmUnavailableExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService problemDetails;
    private readonly ILogger<LlmUnavailableExceptionHandler> logger;

    public LlmUnavailableExceptionHandler(IProblemDetailsService problemDetails, ILogger<LlmUnavailableExceptionHandler> logger)
    {
        this.problemDetails = problemDetails;
        this.logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not LlmProviderException providerException)
        {
            return false;
        }

        logger.LogWarning(providerException, "Model provider unavailable");
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = "30";
        return await problemDetails.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "The model provider is unavailable.",
                    Detail = "Try again shortly. Keyword search (mode=keyword) does not need the model.",
                },
            }
        );
    }
}
