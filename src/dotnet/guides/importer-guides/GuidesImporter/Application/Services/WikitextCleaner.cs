using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GuidesImporter.Application.Helpers;
using GuidesImporter.Application.Services.Interfaces;

namespace GuidesImporter.Application.Services;

/// <summary>
/// Rule-based wikitext to plain text converter tuned for Wikivoyage. It is not a full MediaWiki
/// parser: templates are not expanded from their definitions. Instead, templates that carry
/// travel content (listings, region lists, warning boxes) are rendered by hand, a few inline
/// templates keep their text, and every other template is dropped.
/// </summary>
public sealed partial class WikitextCleaner : IWikitextCleaner
{
    private const int MaxTemplateDepth = 8;

    /// <summary>Listing templates. Each becomes one line of text.</summary>
    private static readonly HashSet<string> ListingTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "listing", "see", "do", "buy", "eat", "drink", "sleep", "go", "around", "learn", "work",
    };

    /// <summary>Inline templates whose first positional argument is the visible text.</summary>
    private static readonly HashSet<string> FirstArgumentTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "iata", "icao", "nowrap", "nobr", "small", "smaller", "big", "smallcaps", "unicode",
        "abbr", "nobreak", "noitalic", "sic", "w", "wikipedia", "transl",
    };

    private static readonly HashSet<string> RemovedLinkNamespaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "file", "image", "media", "category", "template", "user", "special", "help",
    };

    /// <summary>Elements whose content is not prose and is removed with the tags.</summary>
    private static readonly string[] RemovedElements =
    [
        "ref", "gallery", "math", "syntaxhighlight", "source", "mapframe", "maplink", "timeline",
        "score", "graph", "templatedata", "includeonly", "imagemap", "poem",
    ];

    public string Version => "wikitext-cleaner-v1";

    public string Clean(string wikitext)
    {
        if (string.IsNullOrWhiteSpace(wikitext))
        {
            return string.Empty;
        }

        var text = wikitext.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        text = CommentRegex().Replace(text, string.Empty);
        text = RemoveElements(text);
        text = NoWikiRegex().Replace(text, "$1");
        text = ExpandTemplates(text, 0);
        text = RemoveTables(text);
        text = ReplaceLinks(text);
        text = ExternalLinkWithLabelRegex().Replace(text, "$1");
        text = ExternalLinkBareRegex().Replace(text, string.Empty);
        text = BehaviourSwitchRegex().Replace(text, string.Empty);
        text = BoldItalicRegex().Replace(text, string.Empty);
        text = LineBreakTagRegex().Replace(text, "\n");
        text = HtmlTagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        return NormalizeLines(text);
    }

    // ---- Elements ---------------------------------------------------------------------------

    private static string RemoveElements(string text)
    {
        foreach (var name in RemovedElements)
        {
            if (!text.Contains('<' + name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Self-closing form first (<ref name="x" />), then paired form.
            text = Regex.Replace(
                text,
                $@"<{name}\b[^>]*/>",
                string.Empty,
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1)
            );
            text = Regex.Replace(
                text,
                $@"<{name}\b[^>]*>.*?</{name}\s*>",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromSeconds(1)
            );
        }

        return text;
    }

    // ---- Templates --------------------------------------------------------------------------

    private static string ExpandTemplates(string text, int depth)
    {
        if (depth > MaxTemplateDepth)
        {
            return string.Empty;
        }

        return BalancedScanner.ReplaceOutermost(
            text,
            "{{",
            "}}",
            inner => ExpandTemplates(RenderTemplate(inner), depth + 1)
        );
    }

    private static string RenderTemplate(string inner)
    {
        // Template parameters ({{{1}}}) arrive here as "{1}"; they have no meaning in an article.
        if (inner.StartsWith('{'))
        {
            return string.Empty;
        }

        var parts = BalancedScanner.SplitTopLevel(inner, '|');
        var name = parts[0].Trim().Replace('_', ' ');
        var colon = name.IndexOf(':');
        if (colon > 0 && name[..colon].Equals("template", StringComparison.OrdinalIgnoreCase))
        {
            name = name[(colon + 1)..].Trim();
        }

        // Parser functions ({{#if:...}}, {{PAGENAME}}) and magic words are dropped.
        if (name.StartsWith('#') || name.Length == 0)
        {
            return string.Empty;
        }

        var args = TemplateArguments.Parse(parts.Skip(1));
        var key = name.ToLowerInvariant();

        if (ListingTemplates.Contains(key))
        {
            return RenderListing(args);
        }

        if (FirstArgumentTemplates.Contains(key))
        {
            return args.Positional(0);
        }

        return key switch
        {
            "marker" => args.Named("name"),
            "lang" or "lang-ja" or "lang-zh" => args.Positional(1),
            "convert" or "km" or "mi" => JoinNonEmpty(" ", args.Positional(0), args.Positional(1)),
            "regionlist" => RenderRegionList(args),
            "warningbox" => Prefixed("Warning: ", args.Positional(0)),
            "cautionbox" => Prefixed("Caution: ", args.Positional(0)),
            "infobox" => JoinNonEmpty(": ", args.Positional(0), args.Positional(1)),
            "quote" or "quotation" => args.Positional(0),
            _ => string.Empty,
        };
    }

    private static string RenderListing(TemplateArguments args)
    {
        var name = args.Named("name");
        var alt = args.Named("alt");
        var content = args.Named("content");
        if (content.Length == 0)
        {
            content = args.Named("description");
        }

        var builder = new StringBuilder();
        builder.Append(name);
        if (alt.Length > 0)
        {
            builder.Append(builder.Length > 0 ? $" ({alt})" : alt);
        }

        var details = new List<string>();
        AddDetail(details, null, content);
        AddDetail(details, "Address", args.Named("address"));
        AddDetail(details, "Directions", args.Named("directions"));
        AddDetail(details, "Hours", args.Named("hours"));
        AddDetail(details, "Price", args.Named("price"));
        AddDetail(details, "Check-in", args.Named("checkin"));
        AddDetail(details, "Check-out", args.Named("checkout"));

        if (details.Count == 0)
        {
            return builder.ToString();
        }

        if (builder.Length > 0)
        {
            builder.Append(": ");
        }

        builder.Append(string.Join(' ', details));
        return builder.ToString();
    }

    private static void AddDetail(List<string> details, string? label, string value)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            return;
        }

        if (!EndsSentence(value))
        {
            value += ".";
        }

        details.Add(label is null ? value : $"{label}: {value}");
    }

    private static string RenderRegionList(TemplateArguments args)
    {
        var lines = new List<string>();
        for (var i = 1; i <= 30; i++)
        {
            var name = args.Named($"region{i}name");
            if (name.Length == 0)
            {
                continue;
            }

            var description = args.Named($"region{i}description");
            lines.Add(description.Length > 0 ? $"* {name}: {description}" : $"* {name}");
        }

        return lines.Count == 0 ? string.Empty : "\n" + string.Join('\n', lines) + "\n";
    }

    private static string Prefixed(string prefix, string value) =>
        value.Trim().Length == 0 ? string.Empty : prefix + value.Trim();

    private static string JoinNonEmpty(string separator, params string[] values) =>
        string.Join(separator, values.Select(v => v.Trim()).Where(v => v.Length > 0));

    private static bool EndsSentence(string value) =>
        value.Length > 0 && value[^1] is '.' or '!' or '?' or '。' or ')' or '"';

    // ---- Tables -----------------------------------------------------------------------------

    private static string RemoveTables(string text)
    {
        if (!text.Contains("{|", StringComparison.Ordinal))
        {
            return text;
        }

        var output = new StringBuilder(text.Length);
        var depth = 0;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("{|", StringComparison.Ordinal))
            {
                depth++;
                continue;
            }

            if (depth > 0)
            {
                if (trimmed.StartsWith("|}", StringComparison.Ordinal))
                {
                    depth--;
                }

                continue;
            }

            output.Append(line).Append('\n');
        }

        return output.ToString();
    }

    // ---- Links ------------------------------------------------------------------------------

    private static string ReplaceLinks(string text) =>
        BalancedScanner.ReplaceOutermost(text, "[[", "]]", RenderLink);

    private static string RenderLink(string inner)
    {
        var parts = BalancedScanner.SplitTopLevel(inner, '|');
        var target = parts[0].Trim();
        var leadingColon = target.StartsWith(':');
        target = target.TrimStart(':').Trim();

        var colon = target.IndexOf(':');
        if (colon > 0)
        {
            var prefix = target[..colon].Trim();
            if (RemovedLinkNamespaces.Contains(prefix) && !leadingColon)
            {
                return string.Empty;
            }

            // Interlanguage links ([[de:Kyoto]]) render nothing in the article.
            if (parts.Count == 1 && !leadingColon && InterlanguagePrefixRegex().IsMatch(prefix))
            {
                return string.Empty;
            }
        }

        if (parts.Count > 1)
        {
            // Links can nest inside labels of other links only through files, which are removed
            // above, so a single pass is enough here.
            var label = string.Join('|', parts.Skip(1)).Trim();
            return label.Length > 0 ? ReplaceLinks(label) : StripPrefixAndAnchor(target);
        }

        return StripPrefixAndAnchor(target);
    }

    private static string StripPrefixAndAnchor(string target)
    {
        var hash = target.IndexOf('#');
        if (hash >= 0)
        {
            target = hash == 0 ? target[1..] : target[..hash];
        }

        // [[wikipedia:Kyoto]] shows "wikipedia:Kyoto"; the useful part is the title.
        var colon = target.IndexOf(':');
        if (colon > 0 && !target[..colon].Contains(' '))
        {
            target = target[(colon + 1)..];
        }

        return target.Trim();
    }

    // ---- Lines ------------------------------------------------------------------------------

    private static string NormalizeLines(string text)
    {
        var output = new StringBuilder(text.Length);
        var blankRun = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = SpacesRegex().Replace(rawLine.Replace(' ', ' ').Replace('\t', ' '), " ").Trim();

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                var level = Math.Min(heading.Groups["open"].Length, heading.Groups["close"].Length);
                var title = heading.Groups["title"].Value.Trim().Trim('=').Trim();
                if (level < 2 || title.Length == 0)
                {
                    continue;
                }

                var marks = new string('=', Math.Min(level, 6));
                AppendBlank(output, ref blankRun, force: true);
                output.Append(marks).Append(' ').Append(title).Append(' ').Append(marks).Append('\n');
                blankRun = 0;
                continue;
            }

            if (line.StartsWith("----", StringComparison.Ordinal))
            {
                continue;
            }

            var list = ListMarkerRegex().Match(line);
            if (list.Success)
            {
                var item = line[list.Length..].Trim();
                if (item.Length == 0)
                {
                    continue;
                }

                line = "- " + item;
            }

            if (line.Length == 0)
            {
                blankRun++;
                continue;
            }

            // Lines that are only punctuation are leftovers of removed templates.
            if (!line.Any(char.IsLetterOrDigit))
            {
                continue;
            }

            AppendBlank(output, ref blankRun, force: false);
            output.Append(line).Append('\n');
        }

        return output.ToString().Trim();
    }

    private static void AppendBlank(StringBuilder output, ref int blankRun, bool force)
    {
        if (output.Length > 0 && (blankRun > 0 || force) && !EndsWithBlankLine(output))
        {
            output.Append('\n');
        }

        blankRun = 0;
    }

    private static bool EndsWithBlankLine(StringBuilder output) =>
        output.Length >= 2 && output[^1] == '\n' && output[^2] == '\n';

    // ---- Regexes ----------------------------------------------------------------------------

    [GeneratedRegex(@"<!--.*?(-->|\z)", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<nowiki\s*>(.*?)</nowiki\s*>|<nowiki\s*/>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NoWikiRegex();

    [GeneratedRegex(@"\[(?:https?:)?//[^\s\]]+\s+([^\]]+)\]")]
    private static partial Regex ExternalLinkWithLabelRegex();

    [GeneratedRegex(@"\[(?:https?:)?//[^\s\]]+\]")]
    private static partial Regex ExternalLinkBareRegex();

    [GeneratedRegex(@"__[A-Z]+__")]
    private static partial Regex BehaviourSwitchRegex();

    [GeneratedRegex(@"'{2,}")]
    private static partial Regex BoldItalicRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTagRegex();

    [GeneratedRegex(@"</?[a-zA-Z][a-zA-Z0-9]*\b[^>]*>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"^(?<open>=+)\s*(?<title>.*?)\s*(?<close>=+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^[*#:;]+")]
    private static partial Regex ListMarkerRegex();

    [GeneratedRegex(@"[ ]{2,}")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"^[a-z]{2,3}(-[a-z]{2,8})*$")]
    private static partial Regex InterlanguagePrefixRegex();

    /// <summary>Positional and named arguments of one template call.</summary>
    private sealed class TemplateArguments
    {
        private readonly List<string> positional = [];
        private readonly Dictionary<string, string> named = new(StringComparer.OrdinalIgnoreCase);

        public static TemplateArguments Parse(IEnumerable<string> parts)
        {
            var args = new TemplateArguments();
            foreach (var part in parts)
            {
                var equals = part.IndexOf('=');
                var key = equals > 0 ? part[..equals].Trim() : null;
                if (key is not null && key.Length > 0 && key.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-'))
                {
                    args.named[key] = part[(equals + 1)..].Trim();
                }
                else
                {
                    args.positional.Add(part.Trim());
                }
            }

            return args;
        }

        public string Positional(int index)
        {
            if (named.TryGetValue((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), out var value))
            {
                return value;
            }

            return index < positional.Count ? positional[index] : string.Empty;
        }

        public string Named(string key) => named.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
