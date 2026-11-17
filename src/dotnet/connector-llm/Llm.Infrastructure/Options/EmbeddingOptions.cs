using System.ComponentModel.DataAnnotations;

namespace Llm.Infrastructure.Options;

/// <summary>
/// Settings for the embedding client. Bound from the "Embeddings" configuration section. The
/// defaults target a local Ollama server running nomic-embed-text, which needs no API key.
/// </summary>
public class EmbeddingOptions : IValidatableObject
{
    public const string SectionName = "Embeddings";

    /// <summary>
    /// Base URL of an OpenAI-compatible API, ending in "/v1/". Works with Ollama, OpenAI, and
    /// other servers that implement POST /v1/embeddings.
    /// </summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "http://localhost:11434/v1/";

    [Required]
    public string Model { get; set; } = "nomic-embed-text";

    /// <summary>Vector length. Must match the database column, so changing it needs a migration.</summary>
    [Range(1, 16_000)]
    public int Dimensions { get; set; } = 768;

    /// <summary>
    /// Send <see cref="Dimensions"/> in the request. Only for models that can shorten their
    /// vectors on request, such as OpenAI's text-embedding-3 family.
    /// </summary>
    public bool SendDimensions { get; set; }

    /// <summary>
    /// Bearer token. Never put it in appsettings.json: use user secrets or the Embeddings__ApiKey
    /// environment variable. Empty for a local server.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Prepended to stored documents. nomic-embed-text was trained with "search_document: ".</summary>
    public string DocumentPrefix { get; set; } = "search_document: ";

    /// <summary>Prepended to search queries. nomic-embed-text was trained with "search_query: ".</summary>
    public string QueryPrefix { get; set; } = "search_query: ";

    /// <summary>Texts per request.</summary>
    [Range(1, 2048)]
    public int BatchSize { get; set; } = 64;

    /// <summary>Price in US dollars per million input tokens, for cost reporting. 0 for a local model.</summary>
    [Range(typeof(decimal), "0", "1000")]
    public decimal CostPerMillionTokens { get; set; }

    /// <summary>Timeout for one HTTP attempt.</summary>
    [Range(1, 3600)]
    public int AttemptTimeoutSeconds { get; set; } = 120;

    /// <summary>Timeout for one call including every retry.</summary>
    [Range(1, 7200)]
    public int TotalTimeoutSeconds { get; set; } = 600;

    /// <summary>Retries after the first attempt, on timeouts, 408, 429 and 5xx.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 4;

    /// <summary>First retry delay; later delays grow exponentially, with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long the circuit stays open after too many failures.</summary>
    [Range(1, 3600)]
    public int CircuitBreakSeconds { get; set; } = 30;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TotalTimeoutSeconds <= AttemptTimeoutSeconds)
        {
            yield return new ValidationResult(
                "TotalTimeoutSeconds must be greater than AttemptTimeoutSeconds.",
                [nameof(TotalTimeoutSeconds), nameof(AttemptTimeoutSeconds)]
            );
        }

        if (!BaseUrl.EndsWith('/'))
        {
            yield return new ValidationResult("BaseUrl must end with '/'.", [nameof(BaseUrl)]);
        }
    }
}
