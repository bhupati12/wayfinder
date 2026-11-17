using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace SearchService.Application.Services;

/// <summary>
/// Turns an unreachable database into a 503 problem-details response with Retry-After. Without
/// it, a stopped PostgreSQL answered every search with the generic 500 "Something went wrong",
/// which reads as a bug in the API rather than an outage a client should retry.
/// </summary>
public sealed class DatabaseUnavailableExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService problemDetails;
    private readonly ILogger<DatabaseUnavailableExceptionHandler> logger;

    public DatabaseUnavailableExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<DatabaseUnavailableExceptionHandler> logger
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
        if (!IsDatabaseUnavailable(exception))
        {
            return false;
        }

        logger.LogWarning(exception, "Database unavailable");
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = "10";
        return await problemDetails.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "The guide database is unavailable.",
                    Detail = "Try again shortly. Answers that are already cached are still served.",
                },
            }
        );
    }

    /// <summary>
    /// A transient Npgsql failure anywhere in the chain: a refused or dropped connection, a connect
    /// timeout, or a server error that Npgsql marks transient, such as 57P01 when PostgreSQL shuts
    /// down under open connections (the first request after a stop gets that one). EF Core's
    /// retrying strategy wraps it in an InvalidOperationException once retries run out. SQL errors
    /// such as a syntax error are not transient and stay 500s.
    /// </summary>
    public static bool IsDatabaseUnavailable(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is NpgsqlException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }
}
