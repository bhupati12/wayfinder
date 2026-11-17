using System.ComponentModel.DataAnnotations;

namespace GuidesImporter.Application.Options;

/// <summary>
/// Settings for the embedChunks job, bound from the "EmbedChunks" section. Model, batch size and
/// API key belong to the embedding client ("Embeddings" section).
/// </summary>
public class EmbedChunksOptions
{
    public const string SectionName = "EmbedChunks";

    /// <summary>Stop after embedding this many chunks; 0 means all. Useful for a trial run.</summary>
    [Range(0, int.MaxValue)]
    public int MaxChunks { get; set; }

    /// <summary>Log progress every this many requests.</summary>
    [Range(1, int.MaxValue)]
    public int ProgressLogInterval { get; set; } = 25;
}
