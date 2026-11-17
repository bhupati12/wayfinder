using GuidesImporter.Application.Helpers;
using GuidesImporter.Application.Services;
using ICSharpCode.SharpZipLib.BZip2;

namespace GuidesImporter.Tests.UnitTests;

public class DumpReaderTests
{
    [Fact]
    public async Task Reads_every_page_with_its_metadata()
    {
        var pages = await ReadAllAsync(TestSupport.FixturePath("mini-dump.xml"));

        Assert.Equal(6, pages.Count);

        var aioi = Assert.Single(pages, p => p.Title == "Aioi");
        Assert.Equal(381, aioi.PageId);
        Assert.Equal(0, aioi.Namespace);
        Assert.True(aioi.IsArticle);
        Assert.Equal(new DateTimeOffset(2024, 5, 19, 1, 39, 21, TimeSpan.Zero), aioi.Timestamp);
        Assert.Equal(TestSupport.ReadFixture("Aioi.wikitext"), aioi.Text);

        var redirect = Assert.Single(pages, p => p.Title == "Aka Island");
        Assert.Equal("Akajima", redirect.RedirectTarget);
        Assert.False(redirect.IsArticle);

        var talk = Assert.Single(pages, p => p.Title == "Talk:Akajima");
        Assert.Equal(1, talk.Namespace);
        Assert.False(talk.IsArticle);
    }

    [Fact]
    public async Task Reads_bzip2_compressed_dumps()
    {
        var plain = TestSupport.FixturePath("mini-dump.xml");
        var compressed = Path.Combine(Path.GetTempPath(), $"wayfinder-{Guid.NewGuid():N}.xml.bz2");
        try
        {
            await using (var input = File.OpenRead(plain))
            await using (var output = File.Create(compressed))
            {
                BZip2.Compress(input, output, isStreamOwner: false, level: 9);
            }

            var fromPlain = await ReadAllAsync(plain);
            var fromCompressed = await ReadAllAsync(compressed);

            Assert.Equal(fromPlain, fromCompressed);
        }
        finally
        {
            File.Delete(compressed);
        }
    }

    [Fact]
    public async Task Stops_when_cancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ReadAllAsync(TestSupport.FixturePath("mini-dump.xml"), cts.Token)
        );
    }

    [Theory]
    [InlineData("Nara (city)", "https://en.wikivoyage.org/wiki/Nara_(city)")]
    [InlineData("aba_(prefecture)", "https://en.wikivoyage.org/wiki/Aba_(prefecture)")]
    [InlineData("Hong Kong/Kowloon", "https://en.wikivoyage.org/wiki/Hong_Kong/Kowloon")]
    [InlineData("Ōsaka", "https://en.wikivoyage.org/wiki/%C5%8Csaka")]
    public void Builds_source_urls(string title, string expected)
    {
        Assert.Equal(expected, WikiTitles.ToUrl("https://en.wikivoyage.org/wiki/", title));
    }

    private static async Task<List<Application.Models.DumpPage>> ReadAllAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        var pages = new List<Application.Models.DumpPage>();
        await foreach (var page in new DumpReader().ReadPagesAsync(path, cancellationToken))
        {
            pages.Add(page);
        }

        return pages;
    }
}
