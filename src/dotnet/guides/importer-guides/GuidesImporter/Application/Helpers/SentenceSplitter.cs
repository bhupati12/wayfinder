namespace GuidesImporter.Application.Helpers;

/// <summary>
/// Splits a line of prose into sentences. Rule-based and deliberately conservative: when unsure it
/// does not split, because a missed boundary only makes a unit longer, while a false one would cut
/// a sentence in two.
/// </summary>
public static class SentenceSplitter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "st", "mt", "ft", "no", "nos", "vs", "etc", "approx",
        "ave", "rd", "blvd", "hwy", "jr", "sr", "inc", "ltd", "co", "corp", "dept", "est",
        "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec",
        "mon", "tue", "tues", "wed", "thu", "thur", "thurs", "fri", "sat", "sun",
        "e.g", "i.e", "a.m", "p.m", "u.s", "u.k", "cf", "ca", "min", "max", "incl", "excl",
    };

    private const string ClosingMarks = "\"')]»”’";

    public static IReadOnlyList<string> Split(string text)
    {
        var sentences = new List<string>();
        var start = 0;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];
            var isTerminal = c is '.' or '!' or '?' or '。' or '！' or '？';
            if (!isTerminal)
            {
                i++;
                continue;
            }

            // Absorb runs like "?!" or "..." and closing quotes or brackets.
            var end = i + 1;
            while (end < text.Length && (text[end] is '.' or '!' or '?' || ClosingMarks.Contains(text[end])))
            {
                end++;
            }

            // CJK full stops do not need a following space.
            var cjk = c is '。' or '！' or '？';
            if (!cjk)
            {
                if (end >= text.Length || !char.IsWhiteSpace(text[end]))
                {
                    i = end;
                    continue;
                }

                var next = end;
                while (next < text.Length && char.IsWhiteSpace(text[next]))
                {
                    next++;
                }

                if (next >= text.Length || !StartsSentence(text[next]) || (c == '.' && IsAbbreviation(text, i)))
                {
                    i = end;
                    continue;
                }
            }

            var sentence = text[start..end].Trim();
            if (sentence.Length > 0)
            {
                sentences.Add(sentence);
            }

            start = end;
            i = end;
        }

        var rest = text[start..].Trim();
        if (rest.Length > 0)
        {
            sentences.Add(rest);
        }

        return sentences;
    }

    private static bool StartsSentence(char c) =>
        char.IsUpper(c) || char.IsDigit(c) || "\"'(“‘¡¿[".Contains(c) || (char.IsLetter(c) && !char.IsLower(c));

    /// <summary>True when the word ending at the period at <paramref name="dot"/> is an abbreviation or an initial.</summary>
    private static bool IsAbbreviation(string text, int dot)
    {
        var start = dot;
        while (start > 0 && (char.IsLetter(text[start - 1]) || text[start - 1] == '.'))
        {
            start--;
        }

        var word = text[start..dot];
        if (word.Length == 0)
        {
            return false;
        }

        // A single capital letter is an initial ("J. Smith").
        if (word.Length == 1 && char.IsUpper(word[0]))
        {
            return true;
        }

        return Abbreviations.Contains(word);
    }
}
