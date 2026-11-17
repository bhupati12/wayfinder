using System.ComponentModel.DataAnnotations;

namespace SearchService.Application.Options;

/// <summary>API keys for /api/ask, bound from "Auth". Keys come from user secrets or the environment, never appsettings.json.</summary>
public class AuthOptions : IValidatableObject
{
    public const string SectionName = "Auth";

    public List<ApiKeyEntry> ApiKeys { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var entry in ApiKeys)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Key) || entry.Key.Length < 16)
            {
                yield return new ValidationResult(
                    "Every API key needs a Name and a Key of at least 16 characters.",
                    [nameof(ApiKeys)]
                );
            }
        }
    }
}

/// <summary>A named API key. The name identifies the caller in logs and rate limits.</summary>
public class ApiKeyEntry
{
    public string Name { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
}

/// <summary>Rate limits, bound from "RateLimiting". Single-instance (in memory); use Redis for several instances.</summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Questions per API key per minute.</summary>
    [Range(1, 100_000)]
    public int AskPermitsPerMinute { get; set; } = 10;

    /// <summary>Searches per client IP per minute.</summary>
    [Range(1, 1_000_000)]
    public int SearchPermitsPerMinute { get; set; } = 300;
}

/// <summary>Per-request time limits, bound from "RequestTimeouts". The request's cancellation token reaches the database and the model.</summary>
public class ApiTimeoutOptions
{
    public const string SectionName = "RequestTimeouts";

    [Range(1, 600)]
    public int SearchSeconds { get; set; } = 15;

    /// <summary>A local CPU model needs a long time for one answer; a hosted one would allow much less.</summary>
    [Range(1, 3600)]
    public int AskSeconds { get; set; } = 300;
}

/// <summary>Caching, bound from "Caching". Redis when ConnectionStrings:Redis is set, otherwise in-process memory.</summary>
public class CachingOptions
{
    public const string SectionName = "Caching";

    public bool Enabled { get; set; } = true;

    public TimeSpan QueryEmbeddingTtl { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan AnswerTtl { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>Daily spending cap for /api/ask, bound from "CostCap". 0 turns a limit off.</summary>
public class CostCapOptions
{
    public const string SectionName = "CostCap";

    [Range(typeof(decimal), "0", "1000000")]
    public decimal DailyCostCapUsd { get; set; }

    /// <summary>Tokens in plus out per UTC day. Useful with a free local model, where the cost is 0.</summary>
    [Range(0, long.MaxValue)]
    public long DailyTokenCap { get; set; }
}
