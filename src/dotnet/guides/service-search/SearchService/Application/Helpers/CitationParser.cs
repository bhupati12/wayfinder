using System.Text.RegularExpressions;

namespace SearchService.Application.Helpers;

/// <summary>Finds citation numbers such as [1], [2][3] and [1, 4] in an answer.</summary>
public static partial class CitationParser
{
    /// <summary>Distinct citation numbers in order of first appearance.</summary>
    public static IReadOnlyList<int> Parse(string answer)
    {
        var numbers = new List<int>();
        foreach (Match match in CitationRegex().Matches(answer))
        {
            foreach (
                var part in match
                    .Groups["numbers"]
                    .Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            )
            {
                if (int.TryParse(part, out var number) && !numbers.Contains(number))
                {
                    numbers.Add(number);
                }
            }
        }

        return numbers;
    }

    [GeneratedRegex(@"\[(?<numbers>\d{1,3}(?:\s*,\s*\d{1,3})*)\]")]
    private static partial Regex CitationRegex();
}
