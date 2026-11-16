using GuidesImporter.Application.Models;

namespace GuidesImporter.Database.Services.Interfaces;

/// <summary>Hashes stored for an article, used to decide whether to skip, add or update it.</summary>
public sealed record StoredArticleState(long Id, string RawTextHash, string ProcessingSignature);

public interface IGuideArticleRepository
{
    /// <summary>Returns the stored state of the given pages, keyed by page id. Missing pages are absent.</summary>
    Task<IReadOnlyDictionary<long, StoredArticleState>> GetStatesAsync(
        IReadOnlyCollection<long> pageIds,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// In one transaction: inserts <paramref name="added"/>, and for <paramref name="updated"/>
    /// rewrites the article row and replaces all of its chunks. Either everything is written or
    /// nothing is.
    /// </summary>
    Task SaveBatchAsync(
        IReadOnlyCollection<PreparedArticle> added,
        IReadOnlyCollection<PreparedArticle> updated,
        DateTimeOffset ingestedAt,
        CancellationToken cancellationToken
    );
}
