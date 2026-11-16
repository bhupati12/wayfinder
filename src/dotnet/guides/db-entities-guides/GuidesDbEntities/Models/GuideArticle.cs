namespace GuidesDbEntities.Models;

/// <summary>
/// One Wikivoyage article. Its text lives in <see cref="Chunks"/>; this row tracks identity and
/// the hashes that decide whether a re-ingest has to touch it.
/// </summary>
public class GuideArticle
{
    public long Id { get; set; }

    /// <summary>MediaWiki page id from the dump. Stable across renames, so it is the upsert key.</summary>
    public long PageId { get; set; }

    public required string Title { get; set; }

    /// <summary>Link to the source article. Answers must credit Wikivoyage and link here (CC BY-SA).</summary>
    public required string Url { get; set; }

    /// <summary>SHA-256 (hex) of the raw wikitext as it appeared in the dump.</summary>
    public required string RawTextHash { get; set; }

    /// <summary>
    /// SHA-256 (hex) of the cleaner version and chunking settings that built the chunks.
    /// Changing chunk size or overlap changes it, so the next run rebuilds the chunks even though
    /// the article text did not change.
    /// </summary>
    public required string ProcessingSignature { get; set; }

    /// <summary>Revision timestamp from the dump.</summary>
    public DateTimeOffset? RevisionTimestamp { get; set; }

    public DateTimeOffset LastIngestedAt { get; set; }

    public List<GuideChunk> Chunks { get; set; } = [];
}
