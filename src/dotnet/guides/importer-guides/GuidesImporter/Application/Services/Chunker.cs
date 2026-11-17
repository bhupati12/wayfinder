using Llm.Core.Interfaces;
using System.Text;
using System.Text.RegularExpressions;
using GuidesImporter.Application.Helpers;
using GuidesImporter.Application.Models;
using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace GuidesImporter.Application.Services;

/// <summary>
/// Section-aware chunker. The article is turned into a sequence of units (heading lines and
/// sentences) that are packed greedily into chunks of at most
/// <see cref="IngestionOptions.MaxChunkTokens"/> tokens:
/// <list type="bullet">
/// <item>A heading starts a new chunk once the current chunk is big enough: half of
/// <see cref="IngestionOptions.MinChunkTokens"/> for a top-level section ("== Eat =="), the full
/// minimum for a sub-section. Smaller sections are merged with the next one, heading included,
/// instead of becoming fragments.</item>
/// <item>When a chunk is full it is closed at the start of the current line if that leaves it at
/// least the minimum size, so a listing or paragraph stays whole; otherwise between sentences.</item>
/// <item>The last sentences of a full chunk (up to <see cref="IngestionOptions.OverlapTokens"/>)
/// are repeated at the start of the next one, so a fact near a boundary is retrievable from
/// both. Overlap never crosses a heading.</item>
/// <item>Chunks never break inside a sentence, except a single sentence longer than the maximum,
/// which is cut between words.</item>
/// </list>
/// </summary>
public sealed partial class Chunker : IChunker
{
    public const string IntroductionHeading = "Introduction";

    private readonly ITokenCounter tokens;
    private readonly int minTokens;
    private readonly int maxTokens;
    private readonly int overlapTokens;

    /// <summary>Extra tokens a line break adds when units are joined into a chunk.</summary>
    private readonly int newlineTokens;

    public Chunker(ITokenCounter tokens, IOptions<IngestionOptions> options)
    {
        this.tokens = tokens;
        minTokens = options.Value.MinChunkTokens;
        maxTokens = options.Value.MaxChunkTokens;
        overlapTokens = options.Value.OverlapTokens;
        newlineTokens = Math.Max(0, tokens.Count("a\nb") - tokens.Count("a") - tokens.Count("b"));
    }

    public string Signature =>
        $"chunker-v2;tokens={tokens.Name};min={minTokens};max={maxTokens};overlap={overlapTokens}";

    public IReadOnlyList<TextChunk> Chunk(string cleanText)
    {
        var chunks = new List<TextChunk>();
        foreach (var units in Pack(ParseUnits(cleanText)))
        {
            var text = Render(units);
            chunks.Add(new TextChunk(units[0].HeadingPath, chunks.Count, text, tokens.Count(text)));
        }

        return chunks;
    }

    // ---- Parsing ----------------------------------------------------------------------------

    /// <param name="Text">Sentence or heading text.</param>
    /// <param name="Tokens">Token count of <paramref name="Text"/>, plus the line break before it.</param>
    /// <param name="HeadingPath">Heading path in force at this unit, for example "Get in > By plane".</param>
    /// <param name="HeadingLevel">2 to 6 for a heading line, 0 for a sentence.</param>
    /// <param name="StartsLine">True for a heading and for the first sentence of a line.</param>
    private sealed record Unit(string Text, int Tokens, string HeadingPath, int HeadingLevel, bool StartsLine)
    {
        public bool IsHeading => HeadingLevel > 0;
    }

    private List<Unit> ParseUnits(string cleanText)
    {
        var units = new List<Unit>();
        var headingStack = new List<(int Level, string Title)>();
        var path = IntroductionHeading;

        foreach (var rawLine in cleanText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                var level = heading.Groups["marks"].Length;
                var title = heading.Groups["title"].Value.Trim();
                headingStack.RemoveAll(h => h.Level >= level);
                headingStack.Add((level, title));
                path = string.Join(" > ", headingStack.Select(h => h.Title));
                units.Add(new Unit(title, tokens.Count(title) + newlineTokens, path, level, StartsLine: true));
                continue;
            }

            var first = true;
            foreach (var sentence in SentenceSplitter.Split(line))
            {
                foreach (var piece in SplitOversized(sentence))
                {
                    var cost = tokens.Count(piece) + (first ? newlineTokens : 0);
                    units.Add(new Unit(piece, cost, path, HeadingLevel: 0, StartsLine: first));
                    first = false;
                }
            }
        }

        return DropEmptySections(units);
    }

    /// <summary>
    /// Removes headings of sections that have no text of their own, such as the empty "Buy" and
    /// "Connect" sections of an outline article, so they do not pile up as heading-only lines.
    /// </summary>
    private static List<Unit> DropEmptySections(List<Unit> units)
    {
        var kept = new List<Unit>(units.Count);
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (!unit.IsHeading)
            {
                kept.Add(unit);
                continue;
            }

            var hasText = false;
            for (var j = i + 1; j < units.Count && !(units[j].IsHeading && units[j].HeadingLevel <= unit.HeadingLevel); j++)
            {
                if (!units[j].IsHeading)
                {
                    hasText = true;
                    break;
                }
            }

            if (hasText)
            {
                kept.Add(unit);
            }
        }

        return kept;
    }

    /// <summary>Cuts a sentence longer than the maximum between words. Rare: long lists written as one line.</summary>
    private IEnumerable<string> SplitOversized(string sentence)
    {
        var limit = Math.Max(1, maxTokens - newlineTokens);
        if (tokens.Count(sentence) <= limit)
        {
            yield return sentence;
            yield break;
        }

        var piece = new StringBuilder();
        foreach (var word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = piece.Length == 0 ? word : piece + " " + word;
            if (piece.Length > 0 && tokens.Count(candidate) > limit)
            {
                yield return piece.ToString();
                piece.Clear().Append(word);
            }
            else
            {
                piece.Clear().Append(candidate);
            }
        }

        if (piece.Length > 0)
        {
            yield return piece.ToString();
        }
    }

    // ---- Packing ----------------------------------------------------------------------------

    /// <summary>A chunk being built. The first <see cref="OverlapCount"/> units repeat the previous chunk.</summary>
    private sealed class Draft
    {
        public List<Unit> Units { get; } = [];

        public int OverlapCount { get; set; }

        public int Tokens => Units.Sum(u => u.Tokens);

        /// <summary>Tokens of units that are new in this chunk.</summary>
        public int OwnTokens => Units.Skip(OverlapCount).Sum(u => u.Tokens);

        public bool HasOwnText => Units.Skip(OverlapCount).Any(u => !u.IsHeading);
    }

    private List<List<Unit>> Pack(List<Unit> units)
    {
        var chunks = new List<List<Unit>>();
        var draft = new Draft();

        void Emit(Draft d)
        {
            if (d.HasOwnText)
            {
                chunks.Add(d.Units);
            }
        }

        foreach (var unit in units)
        {
            var breakAt = unit.HeadingLevel switch
            {
                0 => int.MaxValue,
                <= 2 => minTokens / 2,
                _ => minTokens,
            };

            if (unit.IsHeading && draft.HasOwnText && draft.OwnTokens >= breakAt)
            {
                // Natural boundary: the section starts a fresh chunk, without overlap.
                Emit(draft);
                draft = new Draft();
            }
            else if (draft.Tokens + unit.Tokens > maxTokens && draft.HasOwnText)
            {
                var (full, next) = Split(draft, unit.StartsLine);
                Emit(full);
                draft = next;
                if (draft.Tokens + unit.Tokens > maxTokens)
                {
                    // Carried text plus this unit does not fit: keep only units that are new.
                    draft.Units.RemoveRange(0, draft.OverlapCount);
                    draft.OverlapCount = 0;
                    if (draft.Tokens + unit.Tokens > maxTokens)
                    {
                        Emit(draft);
                        draft = new Draft();
                    }
                }
            }

            draft.Units.Add(unit);
        }

        // Fold a small tail into the previous chunk when it fits, instead of leaving a fragment.
        var tail = draft.Units.Skip(draft.OverlapCount).ToList();
        if (chunks.Count > 0 && draft.HasOwnText && tail.Sum(u => u.Tokens) < minTokens / 3
            && chunks[^1].Sum(u => u.Tokens) + tail.Sum(u => u.Tokens) <= maxTokens)
        {
            chunks[^1].AddRange(tail);
        }
        else
        {
            Emit(draft);
        }

        return chunks;
    }

    /// <summary>
    /// Splits a full draft into the chunk to emit and the start of the next one. Prefers the start
    /// of the last line (keeping a listing whole); falls back to the last sentence boundary, with
    /// trailing sentences repeated as overlap. Trailing headings always move to the next chunk.
    /// </summary>
    /// <param name="draft">The draft that is full.</param>
    /// <param name="nextStartsLine">Whether the unit that did not fit starts a new line.</param>
    private (Draft Full, Draft Next) Split(Draft draft, bool nextStartsLine)
    {
        var units = draft.Units;
        var end = units.Count;
        while (end > draft.OverlapCount && units[end - 1].IsHeading)
        {
            end--;
        }

        var lineStart = end;
        while (lineStart > draft.OverlapCount && !units[lineStart - 1].StartsLine)
        {
            lineStart--;
        }

        lineStart = Math.Max(lineStart - 1, draft.OverlapCount);
        var cut = end;
        if (!nextStartsLine && lineStart > draft.OverlapCount && lineStart < end && units[lineStart].StartsLine
            && units.Take(lineStart).Sum(u => u.Tokens) >= minTokens)
        {
            cut = lineStart;
        }

        var full = new Draft { OverlapCount = draft.OverlapCount };
        full.Units.AddRange(units.Take(cut));

        // At a line boundary the next chunk starts a new paragraph or listing, so only whole lines
        // are worth repeating. Mid-line, the trailing sentences give the continuation its context.
        var atLineBoundary = cut < end || end < units.Count || nextStartsLine;

        var next = new Draft();
        var overlap = TrailingOverlap(full.Units, wholeLinesOnly: atLineBoundary);
        next.Units.AddRange(overlap);
        next.OverlapCount = overlap.Count;
        next.Units.AddRange(units.Skip(cut));
        return (full, next);
    }

    /// <summary>
    /// The last sentences (or whole lines) of a chunk, up to the overlap budget, stopping at a
    /// heading.
    /// </summary>
    private List<Unit> TrailingOverlap(List<Unit> chunk, bool wholeLinesOnly)
    {
        var overlap = new List<Unit>();
        var pending = new List<Unit>(); // sentences of a line not yet known to fit whole
        var total = 0;
        for (var i = chunk.Count - 1; i >= 0; i--)
        {
            var unit = chunk[i];
            if (unit.IsHeading || total + unit.Tokens > overlapTokens)
            {
                break;
            }

            pending.Insert(0, unit);
            total += unit.Tokens;
            if (!wholeLinesOnly || unit.StartsLine)
            {
                overlap.InsertRange(0, pending);
                pending.Clear();
            }
        }

        return overlap;
    }

    private static string Render(List<Unit> units)
    {
        var builder = new StringBuilder();
        foreach (var unit in units)
        {
            if (builder.Length > 0)
            {
                builder.Append(unit.StartsLine ? '\n' : ' ');
            }

            builder.Append(unit.Text);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^(?<marks>={2,6})\s*(?<title>.+?)\s*=+$")]
    private static partial Regex HeadingRegex();
}
