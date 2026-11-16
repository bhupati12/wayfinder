namespace GuidesDbEntities.Models;

/// <summary>A passage of an article, sized for retrieval (roughly 300 to 500 tokens).</summary>
public class GuideChunk
{
    public long Id { get; set; }

    public long ArticleId { get; set; }

    public GuideArticle? Article { get; set; }

    /// <summary>Heading path of the section the chunk starts in, for example "Get in > By plane".</summary>
    public required string SectionHeading { get; set; }

    /// <summary>Zero-based order of the chunk inside its article.</summary>
    public int Position { get; set; }

    public required string Text { get; set; }

    public int TokenCount { get; set; }

    /// <summary>SHA-256 (hex) of <see cref="Text"/>. Phase 2 uses it to detect stale embeddings.</summary>
    public required string ContentHash { get; set; }
}
