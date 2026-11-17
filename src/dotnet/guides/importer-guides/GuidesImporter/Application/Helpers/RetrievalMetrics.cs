using GuidesImporter.Application.Models;

namespace GuidesImporter.Application.Helpers;

/// <summary>
/// Retrieval metrics over ranked results.
/// <list type="bullet">
/// <item>Recall at K: share of questions whose answer is somewhere in the top K results.</item>
/// <item>Mean reciprocal rank: the average of 1 / rank of the first relevant result (0 when it is
/// not in the list), so finding the answer at rank 1 counts twice as much as rank 2.</item>
/// </list>
/// </summary>
public static class RetrievalMetrics
{
    /// <summary>1-based rank of the first hit from the expected article, or null.</summary>
    public static int? FirstArticleRank(IReadOnlyList<EvalHit> hits, string article)
    {
        var expected = WikiTitles.Normalize(article);
        for (var i = 0; i < hits.Count; i++)
        {
            if (WikiTitles.Normalize(hits[i].ArticleTitle) == expected)
            {
                return i + 1;
            }
        }

        return null;
    }

    /// <summary>
    /// 1-based rank of the first hit from the expected article whose section is the expected one
    /// or one of its sub-sections ("Get in" matches "Get in > By plane"); without an expected
    /// section this equals <see cref="FirstArticleRank"/>.
    /// </summary>
    public static int? FirstSectionRank(IReadOnlyList<EvalHit> hits, string article, string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return FirstArticleRank(hits, article);
        }

        var expected = WikiTitles.Normalize(article);
        for (var i = 0; i < hits.Count; i++)
        {
            if (WikiTitles.Normalize(hits[i].ArticleTitle) == expected && InSection(hits[i].Section, section))
            {
                return i + 1;
            }
        }

        return null;
    }

    public static bool InSection(string actual, string expected) =>
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase)
        || actual.StartsWith(expected + " >", StringComparison.OrdinalIgnoreCase);

    public static double RecallAt(IEnumerable<int?> ranks, int k)
    {
        var list = ranks.ToList();
        return list.Count == 0 ? 0 : list.Count(r => r is { } rank && rank <= k) / (double)list.Count;
    }

    public static double MeanReciprocalRank(IEnumerable<int?> ranks)
    {
        var list = ranks.ToList();
        return list.Count == 0 ? 0 : list.Sum(r => r is { } rank ? 1.0 / rank : 0) / list.Count;
    }

    public static long Median(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
