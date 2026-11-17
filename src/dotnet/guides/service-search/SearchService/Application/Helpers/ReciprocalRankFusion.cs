namespace SearchService.Application.Helpers;

/// <summary>A chunk id with its 1-based rank in each input list and its fused score.</summary>
public sealed record FusedHit(long ChunkId, double Score, int? VectorRank, int? KeywordRank);

/// <summary>
/// Reciprocal rank fusion: score(d) = sum over lists of 1 / (k + rank of d in that list). It uses
/// only ranks, so it can merge lists whose scores are on different scales (cosine similarity and
/// ts_rank) without normalising them, and a chunk found by both methods beats one found by only
/// one of them at a similar rank. <c>keywordWeight</c> scales the keyword list's contribution
/// (weighted RRF); 1 is the unweighted method.
/// </summary>
public static class ReciprocalRankFusion
{
    public static IReadOnlyList<FusedHit> Fuse(
        IReadOnlyList<long> vectorIds,
        IReadOnlyList<long> keywordIds,
        int k,
        int take,
        double keywordWeight = 1.0
    )
    {
        var hits = new Dictionary<long, (double Score, int? VectorRank, int? KeywordRank)>();

        for (var i = 0; i < vectorIds.Count; i++)
        {
            var id = vectorIds[i];
            var current = hits.GetValueOrDefault(id);
            if (current.VectorRank is null)
            {
                hits[id] = (current.Score + 1.0 / (k + i + 1), i + 1, current.KeywordRank);
            }
        }

        for (var i = 0; i < keywordIds.Count; i++)
        {
            var id = keywordIds[i];
            var current = hits.GetValueOrDefault(id);
            if (current.KeywordRank is null)
            {
                hits[id] = (current.Score + keywordWeight / (k + i + 1), current.VectorRank, i + 1);
            }
        }

        return hits.Select(h => new FusedHit(h.Key, h.Value.Score, h.Value.VectorRank, h.Value.KeywordRank))
            .OrderByDescending(h => h.Score)
            .ThenBy(h => Math.Min(h.VectorRank ?? int.MaxValue, h.KeywordRank ?? int.MaxValue))
            .ThenBy(h => h.ChunkId)
            .Take(take)
            .ToList();
    }
}
