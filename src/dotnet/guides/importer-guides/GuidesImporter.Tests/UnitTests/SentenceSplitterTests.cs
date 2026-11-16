using GuidesImporter.Application.Helpers;

namespace GuidesImporter.Tests.UnitTests;

public class SentenceSplitterTests
{
    public static TheoryData<string, string[]> Cases =>
        new()
        {
            { "One. Two.", ["One.", "Two."] },
            { "Is it open? Yes! Go.", ["Is it open?", "Yes!", "Go."] },
            { "Open 9 a.m. to 5 p.m. daily.", ["Open 9 a.m. to 5 p.m. daily."] },
            { "Visit Mt. Fuji. Then rest.", ["Visit Mt. Fuji.", "Then rest."] },
            { "See e.g. the castle. It is old.", ["See e.g. the castle.", "It is old."] },
            { "Ask J. Smith. He knows.", ["Ask J. Smith.", "He knows."] },
            { "Costs ¥3,000. 2 trains daily.", ["Costs ¥3,000.", "2 trains daily."] },
            { "He said \"go.\" Then left.", ["He said \"go.\"", "Then left."] },
            { "Version 2.5 is out.", ["Version 2.5 is out."] },
            { "lower case. continues here.", ["lower case. continues here."] },
            { "京都です。大阪です。", ["京都です。", "大阪です。"] },
            { "No terminal punctuation", ["No terminal punctuation"] },
            { "", new string[0] },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Split_finds_sentence_boundaries(string text, string[] expected)
    {
        Assert.Equal(expected, SentenceSplitter.Split(text));
    }
}
