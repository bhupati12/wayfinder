using System.Text;

namespace GuidesImporter.Application.Helpers;

public static class WikiTitles
{
    /// <summary>
    /// Normalises a title the way MediaWiki does for lookups: underscores become spaces, runs of
    /// whitespace collapse, and the first letter is upper case.
    /// </summary>
    public static string Normalize(string title)
    {
        var builder = new StringBuilder(title.Length);
        var lastWasSpace = false;
        foreach (var ch in title.Trim())
        {
            var c = ch == '_' ? ' ' : ch;
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        var normalized = builder.ToString().Trim();
        if (normalized.Length > 0 && char.IsLower(normalized[0]))
        {
            normalized = char.ToUpperInvariant(normalized[0]) + normalized[1..];
        }

        return normalized;
    }

    /// <summary>Builds the article URL, for example https://en.wikivoyage.org/wiki/Nara_(city).</summary>
    public static string ToUrl(string baseUrl, string title)
    {
        var path = Uri.EscapeDataString(Normalize(title).Replace(' ', '_'))
            .Replace("%2F", "/", StringComparison.Ordinal)
            .Replace("%3A", ":", StringComparison.Ordinal)
            .Replace("%28", "(", StringComparison.Ordinal)
            .Replace("%29", ")", StringComparison.Ordinal)
            .Replace("%2C", ",", StringComparison.Ordinal);

        return baseUrl.EndsWith('/') ? baseUrl + path : baseUrl + "/" + path;
    }
}
