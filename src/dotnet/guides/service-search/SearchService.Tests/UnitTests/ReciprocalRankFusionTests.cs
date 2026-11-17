using SearchService.Application.Helpers;

namespace SearchService.Tests.UnitTests;

public class ReciprocalRankFusionTests
{
    [Fact]
    public void A_chunk_in_both_lists_beats_chunks_in_one_list()
    {
        // 7 is second in both lists; 1 and 2 are first in one list each.
        var fused = ReciprocalRankFusion.Fuse([1, 7, 3], [2, 7, 4], k: 60, take: 10);

        Assert.Equal(7, fused[0].ChunkId);
        Assert.Equal((2, 2), (fused[0].VectorRank, fused[0].KeywordRank));
        Assert.Equal((2.0 / 62), fused[0].Score, 10);
    }

    [Fact]
    public void Scores_are_the_sum_of_reciprocal_ranks()
    {
        var fused = ReciprocalRankFusion.Fuse([10, 20], [20], k: 1, take: 10).ToDictionary(h => h.ChunkId);

        Assert.Equal(1.0 / 2, fused[10].Score, 10); // rank 1 in vector only: 1 / (1 + 1)
        Assert.Equal(1.0 / 3 + 1.0 / 2, fused[20].Score, 10); // rank 2 in vector, rank 1 in keyword
        Assert.Null(fused[10].KeywordRank);
    }

    [Fact]
    public void Ties_go_to_the_better_single_rank()
    {
        // 1 is first in vector, 2 is first in keyword: equal scores, then the id breaks the tie.
        var fused = ReciprocalRankFusion.Fuse([1], [2], k: 60, take: 10);

        Assert.Equal([1L, 2L], fused.Select(h => h.ChunkId));
    }

    [Fact]
    public void Take_limits_the_output_and_empty_inputs_give_nothing()
    {
        Assert.Equal(2, ReciprocalRankFusion.Fuse([1, 2, 3], [4, 5, 6], k: 60, take: 2).Count);
        Assert.Empty(ReciprocalRankFusion.Fuse([], [], k: 60, take: 5));
    }

    [Fact]
    public void Duplicate_ids_in_one_list_count_once_at_their_best_rank()
    {
        var fused = Assert.Single(ReciprocalRankFusion.Fuse([5, 5], [], k: 60, take: 10));
        Assert.Equal(1, fused.VectorRank);
        Assert.Equal(1.0 / 61, fused.Score, 10);
    }
}
