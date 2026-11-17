using System.ComponentModel.DataAnnotations;

namespace Llm.Infrastructure.Options;

/// <summary>
/// Settings for the chat client, bound from the "Chat" section. The defaults target a local
/// Ollama server running qwen2.5:3b, which is free and needs no API key.
/// </summary>
public class ChatOptions : IValidatableObject
{
    public const string SectionName = "Chat";

    /// <summary>Base URL of an OpenAI-compatible API, ending in "/v1/".</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "http://localhost:11434/v1/";

    [Required]
    public string Model { get; set; } = "qwen2.5:3b";

    /// <summary>Bearer token from user secrets or Chat__ApiKey; empty for a local server.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Tokens the model can attend to, prompt plus answer. Ollama defaults to 4096 unless the
    /// model is configured otherwise; the answer service budgets sources to fit.
    /// </summary>
    [Range(512, 1_000_000)]
    public int ContextWindowTokens { get; set; } = 4096;

    [Range(1, 32_000)]
    public int MaxOutputTokens { get; set; } = 400;

    /// <summary>Low temperature keeps grounded answers close to the sources.</summary>
    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.1;

    [Range(typeof(decimal), "0", "1000")]
    public decimal CostPerMillionInputTokens { get; set; }

    [Range(typeof(decimal), "0", "1000")]
    public decimal CostPerMillionOutputTokens { get; set; }

    /// <summary>
    /// Timeout for one attempt until response headers arrive (for streaming, until the first
    /// byte). A local CPU model reads a long prompt slowly, so this is generous.
    /// </summary>
    [Range(1, 3600)]
    public int AttemptTimeoutSeconds { get; set; } = 180;

    [Range(1, 7200)]
    public int TotalTimeoutSeconds { get; set; } = 400;

    [Range(0, 10)]
    public int MaxRetries { get; set; } = 1;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

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

        if (MaxOutputTokens >= ContextWindowTokens)
        {
            yield return new ValidationResult(
                "MaxOutputTokens must be smaller than ContextWindowTokens.",
                [nameof(MaxOutputTokens)]
            );
        }

        if (!BaseUrl.EndsWith('/'))
        {
            yield return new ValidationResult("BaseUrl must end with '/'.", [nameof(BaseUrl)]);
        }
    }
}
