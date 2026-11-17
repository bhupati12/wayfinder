using System.ComponentModel.DataAnnotations;

namespace DomainModels.Guides.Models;

/// <summary>Body of POST /api/ask and POST /api/ask/stream.</summary>
public sealed class AskRequest
{
    [Required]
    [StringLength(1000, MinimumLength = 3)]
    public string? Question { get; set; }

    /// <summary>Chunks to retrieve before budgeting; the configured default when omitted.</summary>
    [Range(1, 20)]
    public int? K { get; set; }
}

/// <summary>A grounded answer with the sources it cites.</summary>
public sealed record AskResponse
{
    public required string Question { get; init; }

    public required string Answer { get; init; }

    /// <summary>False when the model said the sources do not cover the question.</summary>
    public required bool Answered { get; init; }

    /// <summary>Sources the answer cites, in citation-number order.</summary>
    public required IReadOnlyList<Citation> Citations { get; init; }

    public required AnswerMetadata Metadata { get; init; }

    public string Attribution { get; init; } = SearchResponse.WikivoyageAttribution;
}

/// <summary>One numbered source shown to the model.</summary>
public sealed record Citation(int Number, long ChunkId, string ArticleTitle, string Section, string SourceUrl);

/// <summary>What the answer cost and how long each step took.</summary>
public sealed record AnswerMetadata
{
    public required string PromptVersion { get; init; }

    public required string Model { get; init; }

    public required int SourcesRetrieved { get; init; }

    /// <summary>Sources that fitted the token budget and were sent to the model.</summary>
    public required int SourcesUsed { get; init; }

    /// <summary>True when retrieval fell back to keyword search.</summary>
    public bool RetrievalDegraded { get; init; }

    public required int InputTokens { get; init; }

    public required int OutputTokens { get; init; }

    /// <summary>True when the provider reported no usage and the counts are estimates.</summary>
    public bool TokensEstimated { get; init; }

    /// <summary>Citation numbers in the answer that matched no source; they are dropped.</summary>
    public int InvalidCitations { get; init; }

    public required long RetrievalMilliseconds { get; init; }

    public required long GenerationMilliseconds { get; init; }

    public required long TotalMilliseconds { get; init; }

    public required decimal EstimatedCostUsd { get; init; }
}
