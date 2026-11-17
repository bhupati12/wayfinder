using GuidesImporter.Application.Services;

namespace GuidesImporter.Tests.UnitTests;

public class WikitextCleanerTests
{
    private readonly WikitextCleaner cleaner = new();

    public static TheoryData<string, string, string> MarkupCases =>
        new()
        {
            { "bold and italic", "'''Akita''' is ''very'' nice.", "Akita is very nice." },
            { "internal link", "Fly to [[Tokyo]].", "Fly to Tokyo." },
            { "piped link", "Take the [[Osaka|Shin-Osaka]] train.", "Take the Shin-Osaka train." },
            { "link with anchor", "See [[Kyoto#Eat]].", "See Kyoto." },
            { "link trail", "Many [[temple]]s.", "Many temples." },
            { "interwiki keeps title", "Read [[wikipedia:Kyoto]].", "Read Kyoto." },
            { "interlanguage link removed", "Text.\n[[de:Kyoto]]", "Text." },
            { "file removed with nested link", "[[File:A.jpg|thumb|A [[Kyoto]] temple]]Text.", "Text." },
            { "image removed", "[[Image:Akajima1.jpg|thumb|400px|Bridge]]\nText.", "Text." },
            { "category removed", "Text.[[Category:Japan]]", "Text." },
            { "external link with label", "Visit [https://aioi.in/ the guide site].", "Visit the guide site." },
            { "bare external link removed", "Visit [https://aioi.in/] now.", "Visit now." },
            { "comment removed", "Open<!-- hidden --> daily.", "Open daily." },
            { "unterminated comment removed", "Open daily.<!-- * {{marker", "Open daily." },
            { "reference removed", "Open daily.<ref>Source</ref>", "Open daily." },
            { "self-closing reference removed", "Open daily.<ref name=\"a\" />", "Open daily." },
            { "html tags unwrapped", "Open <span class=\"x\">daily</span>.", "Open daily." },
            { "line break tag", "Line one<br/>Line two", "Line one\nLine two" },
            { "html entities decoded", "Fish &amp; chips&nbsp;here.", "Fish & chips here." },
            { "unknown template removed", "{{pagebanner|Banner.jpg}}Text.", "Text." },
            { "nested unknown template removed", "{{a|{{b|c}}}}Text.", "Text." },
            { "parser function removed", "{{#if:x|y}}Text.", "Text." },
            { "IATA keeps code", "Fly into {{IATA|AXT}}.", "Fly into AXT." },
            { "lang keeps text", "Say {{lang|ja|こんにちは}}.", "Say こんにちは." },
            { "warning box", "{{warningbox|Swimming is dangerous here.}}", "Warning: Swimming is dangerous here." },
            { "marker keeps name", "* {{marker|type=city|name=[[Barkam]]|wikidata=Q1}} — a town", "- Barkam — a town" },
            { "table removed", "Before.\n{| class=\"wikitable\"\n|-\n| a || b\n|}\nAfter.", "Before.\nAfter." },
            { "nested table removed", "{|\n|\n{|\n| x\n|}\n|}\nAfter.", "After." },
            { "heading normalised", "==Get in==\nText.", "== Get in ==\nText." },
            { "sub-heading normalised", "=== By plane ===\nText.", "=== By plane ===\nText." },
            { "list markers", "* one\n** two\n# three", "- one\n- two\n- three" },
            { "empty list item dropped", "* {{mapframe}}\n* real", "- real" },
            { "behaviour switch removed", "__NOTOC__Text.", "Text." },
            { "blank lines collapsed", "One.\n\n\n\nTwo.", "One.\n\nTwo." },
        };

    [Theory]
    [MemberData(nameof(MarkupCases))]
    public void Clean_converts_markup(string because, string input, string expected)
    {
        Assert.True(expected == cleaner.Clean(input), $"{because}: got \"{cleaner.Clean(input)}\"");
    }

    public static TheoryData<string, string> ListingCases =>
        new()
        {
            {
                "{{see | name=Kanjōsan Castle Ruins | alt=感状山城跡 | url=https://x | content=Remains of a castle built in the 13th century.}}",
                "Kanjōsan Castle Ruins (感状山城跡): Remains of a castle built in the 13th century."
            },
            {
                "{{eat|name=Peacock|address=Yabase 4|hours=11:00-22:00|price=¥1,000|content=Indian food}}",
                "Peacock: Indian food. Address: Yabase 4. Hours: 11:00-22:00. Price: ¥1,000."
            },
            { "{{go\n| name=Aioi Station | alt= | url= \n| content=\n}}", "Aioi Station" },
            { "{{sleep|name=Inn|checkin=15:00|checkout=10:00}}", "Inn: Check-in: 15:00. Check-out: 10:00." },
            { "{{listing|name=[[Himeji Castle]]|content=See it.}}", "Himeji Castle: See it." },
            { "{{do|name=Dive|content=Call {{phone|123}} first.}}", "Dive: Call first." },
        };

    [Theory]
    [MemberData(nameof(ListingCases))]
    public void Clean_renders_listings_as_one_line(string input, string expected)
    {
        Assert.Equal(expected, cleaner.Clean(input));
    }

    [Fact]
    public void Clean_renders_region_list()
    {
        var text = cleaner.Clean(
            "{{Regionlist\n| region1name=[[Kanto]]\n| region1description=Tokyo and around.\n| region2name=Kansai\n}}"
        );

        Assert.Equal("- Kanto: Tokyo and around.\n- Kansai", text);
    }

    [Theory]
    [InlineData("Aioi.wikitext")]
    [InlineData("Akajima.wikitext")]
    [InlineData("Aba_(prefecture).wikitext")]
    public void Clean_removes_all_markup_from_real_articles(string fixture)
    {
        var text = cleaner.Clean(TestSupport.ReadFixture(fixture));

        Assert.NotEmpty(text);
        Assert.DoesNotContain("{{", text);
        Assert.DoesNotContain("}}", text);
        Assert.DoesNotContain("[[", text);
        Assert.DoesNotContain("]]", text);
        Assert.DoesNotContain("''", text);
        Assert.DoesNotContain("<!--", text);
        Assert.DoesNotContain("wikidata", text);
        Assert.DoesNotContain("lastedit", text);
        Assert.DoesNotContain(".jpg", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clean_keeps_content_of_a_real_article()
    {
        var text = cleaner.Clean(TestSupport.ReadFixture("Aioi.wikitext"));

        Assert.StartsWith("Aioi (相生) is a small port city in western Hyogo.", text);
        Assert.Contains("== See ==", text);
        Assert.Contains(
            "- Kanjōsan Castle Ruins (感状山城跡): Remains of a Muromachi period castle built in the 13th century.",
            text
        );
        Assert.Contains("- Aioi Station", text);
        Assert.Contains("- Himeji", text);
        Assert.Contains("Japanese-only guide site", text);
        Assert.DoesNotContain("Shin-Osaka", text); // inside the removed routebox
    }

    [Fact]
    public void Clean_drops_commented_out_listings()
    {
        var text = cleaner.Clean(TestSupport.ReadFixture("Aba_(prefecture).wikitext"));

        Assert.Contains("- Langmusi — a sleepy village", text);
        Assert.DoesNotContain("Heishui", text); // commented out in the source
    }

    [Fact]
    public void Clean_is_deterministic()
    {
        var source = TestSupport.ReadFixture("Akajima.wikitext");
        Assert.Equal(cleaner.Clean(source), cleaner.Clean(source.Replace("\n", "\r\n")));
    }
}
