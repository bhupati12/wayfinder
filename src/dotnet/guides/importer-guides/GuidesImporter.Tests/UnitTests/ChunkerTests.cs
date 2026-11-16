using GuidesImporter.Application.Helpers;
using GuidesImporter.Application.Services;
using static GuidesImporter.Tests.TestSupport;

namespace GuidesImporter.Tests.UnitTests;

/// <summary>
/// Table-driven cases use <see cref="WordTokenCounter"/> (one token per word) so sizes are exact.
/// The real-article cases use the production cl100k_base tokenizer.
/// </summary>
public class ChunkerTests
{
    private readonly WordTokenCounter words = new();

    [Fact]
    public void Short_text_is_one_introduction_chunk()
    {
        var chunks = Chunker(words, min: 20, max: 40, overlap: 5).Chunk("Akita is a city. It is in Japan.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("Introduction", chunk.SectionHeading);
        Assert.Equal(0, chunk.Position);
        Assert.Equal("Akita is a city. It is in Japan.", chunk.Text);
        Assert.Equal(8, chunk.TokenCount);
    }

    [Fact]
    public void Empty_text_gives_no_chunks()
    {
        Assert.Empty(Chunker(words, 20, 40, 5).Chunk(""));
        Assert.Empty(Chunker(words, 20, 40, 5).Chunk("== Connect ==\n\n== Go next =="));
    }

    public static TheoryData<string, string, string[]> SectionCases =>
        new()
        {
            {
                "small sections merge with their headings",
                $"{Sentence("x", 3)}\n== A ==\n{Sentence("a", 3)}\n== B ==\n{Sentence("b", 3)}",
                ["Introduction"]
            },
            {
                "sections over half the minimum start their own chunk",
                $"{Sentence("x", 12)}\n== A ==\n{Sentence("a", 12)}\n== B ==\n{Sentence("b", 12)}",
                ["Introduction", "A", "B"]
            },
            {
                "a small sub-section stays with its parent",
                $"== A ==\n{Sentence("a", 15)}\n=== A1 ===\n{Sentence("b", 6)}",
                ["A"]
            },
            {
                "a sub-section starts a chunk once the minimum is reached",
                $"== A ==\n{Sentence("a", 25)}\n=== A1 ===\n{Sentence("b", 6)}",
                ["A", "A > A1"]
            },
            {
                "heading paths nest",
                $"== Get in ==\n{Sentence("a", 25)}\n=== By plane ===\n{Sentence("b", 25)}\n=== By train ===\n{Sentence("c", 25)}",
                ["Get in", "Get in > By plane", "Get in > By train"]
            },
        };

    [Theory]
    [MemberData(nameof(SectionCases))]
    public void Chunks_follow_sections(string because, string text, string[] expectedHeadings)
    {
        var chunks = Chunker(words, min: 20, max: 40, overlap: 5).Chunk(text);

        Assert.True(
            expectedHeadings.SequenceEqual(chunks.Select(c => c.SectionHeading)),
            $"{because}: got [{string.Join(", ", chunks.Select(c => c.SectionHeading))}]"
        );
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Position));
    }

    [Fact]
    public void Merged_sections_keep_their_heading_lines_in_the_text()
    {
        var chunk = Assert.Single(
            Chunker(words, 20, 40, 5).Chunk($"{Sentence("x", 3)}\n== A ==\n{Sentence("a", 3)}")
        );

        Assert.Equal("X1 x2 x3.\nA\nA1 a2 a3.", chunk.Text);
    }

    [Fact]
    public void Long_paragraph_is_split_between_sentences_with_overlap()
    {
        // Twelve five-word sentences on one line: 60 tokens, maximum 40, overlap budget 8.
        var sentences = Enumerable.Range(1, 12).Select(i => Sentence($"s{i}w", 5)).ToList();

        var chunks = Chunker(words, min: 20, max: 40, overlap: 8).Chunk(string.Join(' ', sentences));

        Assert.Equal(2, chunks.Count);
        Assert.Equal(string.Join(' ', sentences.Take(8)), chunks[0].Text);
        // The last sentence of the first chunk is repeated (5 tokens fits the 8-token budget; 10 does not).
        Assert.Equal(string.Join(' ', sentences.Skip(7)), chunks[1].Text);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 40));
    }

    [Fact]
    public void Full_chunk_breaks_at_the_start_of_a_line_to_keep_a_listing_whole()
    {
        var line1 = Sentence("a", 10) + " " + Sentence("b", 10); // 20
        var line2 = Sentence("c", 8) + " " + Sentence("d", 8); // 16
        var line3 = Sentence("e", 4) + " " + Sentence("f", 4) + " " + Sentence("g", 4); // 12

        var chunks = Chunker(words, min: 20, max: 40, overlap: 8).Chunk($"{line1}\n{line2}\n{line3}");

        Assert.Equal(2, chunks.Count);
        Assert.Equal($"{line1}\n{line2}", chunks[0].Text);
        // No overlap: at a line boundary only whole lines are repeated, and line 2 exceeds the budget.
        Assert.Equal(line3, chunks[1].Text);
    }

    [Fact]
    public void Whole_short_line_is_repeated_as_overlap_at_a_line_boundary()
    {
        var long1 = Sentence("a", 18);
        var short2 = Sentence("b", 4);
        var long3 = Sentence("c", 20);

        var chunks = Chunker(words, min: 20, max: 30, overlap: 6).Chunk($"{long1}\n{short2}\n{long3}");

        Assert.Equal(2, chunks.Count);
        Assert.Equal($"{long1}\n{short2}", chunks[0].Text);
        Assert.Equal($"{short2}\n{long3}", chunks[1].Text);
    }

    [Fact]
    public void Heading_at_the_end_of_a_full_chunk_moves_to_the_next_chunk()
    {
        var text = $"== A ==\n{Sentence("a", 15)}\n=== A1 ===\n{Sentence("b", 30)}";

        var chunks = Chunker(words, min: 20, max: 40, overlap: 5).Chunk(text);

        Assert.Equal(2, chunks.Count);
        Assert.Equal($"A\n{Sentence("a", 15)}", chunks[0].Text);
        Assert.Equal($"A1\n{Sentence("b", 30)}", chunks[1].Text);
        Assert.Equal("A > A1", chunks[1].SectionHeading);
    }

    [Fact]
    public void Small_last_section_is_folded_into_the_previous_chunk()
    {
        // The 15-token introduction is big enough to close at "Go next", but the 4-token tail is
        // below a third of the minimum and fits, so it joins the introduction instead.
        var text = $"{Sentence("x", 15)}\n== Go next ==\n{Sentence("y", 3)}";

        var chunk = Assert.Single(Chunker(words, min: 20, max: 40, overlap: 5).Chunk(text));
        Assert.Equal("Introduction", chunk.SectionHeading);
        Assert.Equal($"{Sentence("x", 15)}\nGo next\n{Sentence("y", 3)}", chunk.Text);
    }

    [Fact]
    public void Headings_of_empty_sections_are_dropped()
    {
        var text = $"{Sentence("x", 3)}\n== Do ==\n== Buy ==\n=== Malls ===\n== Go next ==\n{Sentence("y", 3)}";

        var chunk = Assert.Single(Chunker(words, min: 20, max: 40, overlap: 5).Chunk(text));
        Assert.Equal($"{Sentence("x", 3)}\nGo next\n{Sentence("y", 3)}", chunk.Text);
    }

    [Fact]
    public void Sentence_longer_than_the_maximum_is_cut_between_words()
    {
        var sentence = Sentence("w", 25);

        var chunks = Chunker(words, min: 5, max: 10, overlap: 2).Chunk(sentence);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 10));
        Assert.Equal(sentence, string.Join(' ', chunks.Select(c => c.Text)));
    }

    [Fact]
    public void Signature_changes_with_settings()
    {
        Assert.NotEqual(Chunker(words, 300, 500, 50).Signature, Chunker(words, 300, 500, 60).Signature);
        Assert.Equal(Chunker(words, 300, 500, 50).Signature, Chunker(words, 300, 500, 50).Signature);
    }

    // ---- Real articles, real tokenizer ------------------------------------------------------

    public static TheoryData<string, int, int, int> RealArticleCases =>
        new()
        {
            { "Aba_(prefecture).wikitext", 300, 500, 50 },
            { "Akajima.wikitext", 300, 500, 50 },
            { "Aioi.wikitext", 300, 500, 50 },
            { "Aba_(prefecture).wikitext", 60, 120, 20 },
            { "Akajima.wikitext", 60, 120, 20 },
        };

    [Theory]
    [MemberData(nameof(RealArticleCases))]
    public void Real_article_chunks_respect_the_limits_and_cover_every_sentence(string fixture, int min, int max, int overlap)
    {
        var tokenizer = new TiktokenTokenCounter();
        var clean = new WikitextCleaner().Clean(ReadFixture(fixture));

        var chunks = Chunker(tokenizer, min, max, overlap).Chunk(clean);

        Assert.NotEmpty(chunks);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Position));
        Assert.All(chunks, c => Assert.InRange(c.TokenCount, 1, max));
        Assert.All(chunks, c => Assert.Equal(tokenizer.Count(c.Text), c.TokenCount));

        var lines = clean.Split('\n').Where(l => l.Length > 0 && !l.StartsWith("==", StringComparison.Ordinal)).ToList();
        // Sentences longer than the maximum are cut between words, so only shorter ones appear whole.
        foreach (var sentence in lines.SelectMany(SentenceSplitter.Split).Where(s => tokenizer.Count(s) < max))
        {
            Assert.True(
                chunks.Any(c => c.Text.Contains(sentence, StringComparison.Ordinal)),
                $"No chunk contains the sentence: {sentence}"
            );
        }

        // Every chunk ends where a sentence or a line ends, never inside a sentence.
        foreach (var chunk in chunks)
        {
            var lastLine = chunk.Text.Split('\n')[^1];
            var lastSentence = SentenceSplitter.Split(lastLine)[^1];
            Assert.True(
                lines.Any(l => l.EndsWith(lastSentence, StringComparison.Ordinal) || l.Contains(lastSentence + " ", StringComparison.Ordinal)),
                $"Chunk {chunk.Position} ends inside a sentence: ...{lastSentence}"
            );
        }
    }
}
