using System.Net;

namespace Llm.Core.Exceptions;

/// <summary>An LLM or embedding provider returned an error, or an unusable response, after retries.</summary>
public class LlmProviderException : Exception
{
    public LlmProviderException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status of the final attempt, when there was a response.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// True when retrying later may succeed (timeouts, rate limits, 5xx, open circuit). False when
    /// the request itself is wrong (400, 401, 404) and will fail again.
    /// </summary>
    public bool IsTransient =>
        StatusCode is null
        || StatusCode == HttpStatusCode.RequestTimeout
        || StatusCode == HttpStatusCode.TooManyRequests
        || (int)StatusCode >= 500;
}
