using Llm.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SearchService.Application.Services.Interfaces;

namespace SearchService.Application.Services;

/// <summary>
/// Turns a failing model provider, or a reached cost cap, into a 503 problem-details response
/// with Retry-After instead of a 500.
/// </summary>
public sealed class LlmUnavailableExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService problemDetails;
    private readonly ILogger<LlmUnavailableExceptionHandler> logger;

    public LlmUnavailableExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<LlmUnavailableExceptionHandler> logger
    )
    {
        this.problemDetails = problemDetails;
        this.logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        var (title, detail, retryAfter) = exception switch
        {
            LlmProviderException => (
                "The model provider is unavailable.",
                "Try again shortly. Keyword search (mode=keyword) does not need the model.",
                "30"
            ),
            CostCapExceededException cap => (
                "Answers are paused for today.",
                cap.Message + " Search still works.",
                SecondsUntilUtcMidnight()
            ),
            _ => default,
        };

        if (title is null)
        {
            return false;
        }

        logger.LogWarning(exception, "Answering unavailable: {Title}", title);
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = retryAfter;
        return await problemDetails.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = title,
                    Detail = detail,
                },
            }
        );
    }

    private static string SecondsUntilUtcMidnight()
    {
        var now = DateTimeOffset.UtcNow;
        return ((int)(now.Date.AddDays(1) - now.UtcDateTime).TotalSeconds).ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );
    }
}

/// <summary>
/// Last-resort handler: logs the exception and returns a generic 500 problem. The exception
/// message and stack trace never reach the client; the correlation id lets support find the log.
/// </summary>
public sealed class FallbackExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService problemDetails;
    private readonly ILogger<FallbackExceptionHandler> logger;

    public FallbackExceptionHandler(IProblemDetailsService problemDetails, ILogger<FallbackExceptionHandler> logger)
    {
        this.problemDetails = problemDetails;
        this.logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
            httpContext.Response.StatusCode = 499;
            return true;
        }

        logger.LogError(exception, "Unhandled exception");
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Something went wrong.",
                    Detail = "The error has been logged. Quote the correlationId when reporting it.",
                },
            }
        );
    }
}
