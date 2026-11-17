using System.Text;

namespace GuidesImporter.Application.Helpers;

/// <summary>Finds balanced, possibly nested, delimiters such as {{ }} and [[ ]] in wikitext.</summary>
public static class BalancedScanner
{
    /// <summary>
    /// Replaces every outermost <paramref name="open"/>...<paramref name="close"/> span with the
    /// result of <paramref name="replace"/>, which receives the inner text. An opener with no
    /// matching closer is dropped and the text after it is kept.
    /// </summary>
    public static string ReplaceOutermost(string text, string open, string close, Func<string, string> replace)
    {
        if (!text.Contains(open, StringComparison.Ordinal))
        {
            return text;
        }

        var output = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var openAt = text.IndexOf(open, i, StringComparison.Ordinal);
            if (openAt < 0)
            {
                output.Append(text, i, text.Length - i);
                break;
            }

            output.Append(text, i, openAt - i);
            var closeAt = FindClose(text, openAt + open.Length, open, close);
            if (closeAt < 0)
            {
                // Unbalanced: drop the opener and carry on.
                i = openAt + open.Length;
                continue;
            }

            var inner = text.Substring(openAt + open.Length, closeAt - openAt - open.Length);
            output.Append(replace(inner));
            i = closeAt + close.Length;
        }

        return output.ToString();
    }

    /// <summary>
    /// Splits on <paramref name="separator"/> at depth zero, ignoring separators inside nested
    /// {{ }} or [[ ]].
    /// </summary>
    public static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (
                i + 1 < text.Length
                && ((text[i] == '{' && text[i + 1] == '{') || (text[i] == '[' && text[i + 1] == '['))
            )
            {
                depth++;
                i++;
            }
            else if (
                i + 1 < text.Length
                && depth > 0
                && ((text[i] == '}' && text[i + 1] == '}') || (text[i] == ']' && text[i + 1] == ']'))
            )
            {
                depth--;
                i++;
            }
            else if (text[i] == separator && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static int FindClose(string text, int from, string open, string close)
    {
        var depth = 1;
        var i = from;
        while (i < text.Length)
        {
            if (string.CompareOrdinal(text, i, close, 0, close.Length) == 0)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }

                i += close.Length;
            }
            else if (string.CompareOrdinal(text, i, open, 0, open.Length) == 0)
            {
                depth++;
                i += open.Length;
            }
            else
            {
                i++;
            }
        }

        return -1;
    }
}
