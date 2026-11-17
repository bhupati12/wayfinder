namespace DomainModels.Guides.Models;

/// <summary>
/// The exact text that is embedded for a chunk. A chunk on its own often lacks its subject ("The
/// castle opens at 9:00"), so the article title and section are put in front of it; this helps
/// vector search match questions that name the place.
/// </summary>
public static class ChunkEmbeddingText
{
    /// <summary>Bump when <see cref="Build"/> changes, so every chunk is re-embedded.</summary>
    public const string Version = "embedding-text-v1";

    public static string Build(string articleTitle, string sectionHeading, string text) =>
        $"{articleTitle} — {sectionHeading}\n\n{text}";
}
