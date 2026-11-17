using System.ComponentModel.DataAnnotations;

namespace GuidesImporter.Application.Options;

/// <summary>Settings for the ingestGuides job. Bound from the "Ingestion" configuration section.</summary>
public class IngestionOptions : IValidatableObject
{
    public const string SectionName = "Ingestion";

    /// <summary>
    /// Path to the Wikivoyage pages-articles dump, either .xml or .xml.bz2. A relative path is
    /// resolved against the application's content root.
    /// </summary>
    [Required]
    public string DumpPath { get; set; } = string.Empty;

    /// <summary>
    /// Only ingest articles under these regions in the IsPartOf hierarchy (for example
    /// "East Asia"). Empty means every article in the dump. Filtering by region costs one extra
    /// pass over the dump.
    /// </summary>
    public string[] Regions { get; set; } = [];

    /// <summary>Stop after this many eligible articles. 0 means no limit.</summary>
    [Range(0, int.MaxValue)]
    public int ArticleLimit { get; set; } = 5000;

    /// <summary>A chunk is closed at a sub-heading once it has at least this many tokens.</summary>
    [Range(1, 8192)]
    public int MinChunkTokens { get; set; } = 300;

    /// <summary>Hard upper bound on chunk size in tokens.</summary>
    [Range(1, 8192)]
    public int MaxChunkTokens { get; set; } = 500;

    /// <summary>Tokens of trailing sentences repeated at the start of the next chunk.</summary>
    [Range(0, 4096)]
    public int OverlapTokens { get; set; } = 50;

    /// <summary>Articles whose cleaned text is shorter than this (outlines, stubs) are ignored.</summary>
    [Range(0, int.MaxValue)]
    public int MinArticleTokens { get; set; } = 50;

    /// <summary>Articles written to the database per transaction.</summary>
    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 100;

    /// <summary>Log progress every this many eligible articles.</summary>
    [Range(1, int.MaxValue)]
    public int ProgressLogInterval { get; set; } = 500;

    /// <summary>Base URL of source articles; the title is appended.</summary>
    [Required, Url]
    public string SourceBaseUrl { get; set; } = "https://en.wikivoyage.org/wiki/";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinChunkTokens > MaxChunkTokens)
        {
            yield return new ValidationResult(
                "MinChunkTokens must not exceed MaxChunkTokens.",
                [nameof(MinChunkTokens), nameof(MaxChunkTokens)]
            );
        }

        if (OverlapTokens >= MinChunkTokens)
        {
            yield return new ValidationResult(
                "OverlapTokens must be smaller than MinChunkTokens.",
                [nameof(OverlapTokens), nameof(MinChunkTokens)]
            );
        }
    }
}
