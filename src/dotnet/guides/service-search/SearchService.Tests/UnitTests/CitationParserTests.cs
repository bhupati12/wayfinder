using SearchService.Application.Helpers;

namespace SearchService.Tests.UnitTests;

public class CitationParserTests
{
    public static TheoryData<string, int[]> Cases =>
        new()
        {
            { "Try the ramen [1].", [1] },
            { "Take the train [2][3]. Eat ramen [1].", [2, 3, 1] },
            { "Both [1, 3] and [3].", [1, 3] },
            { "No citations here.", [] },
            { "Prices in [brackets] and [1a] are not citations.", [] },
            { "Big numbers [12] work.", [12] },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Finds_distinct_citation_numbers_in_order(string answer, int[] expected)
    {
        Assert.Equal(expected, CitationParser.Parse(answer));
    }
}
