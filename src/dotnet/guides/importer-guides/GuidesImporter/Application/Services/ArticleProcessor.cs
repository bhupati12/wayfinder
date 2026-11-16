using GuidesImporter.Application.Helpers;
using GuidesImporter.Application.Models;
using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace GuidesImporter.Application.Services;

public sealed class ArticleProcessor : IArticleProcessor
{
    private readonly IWikitextCleaner cleaner;
    private readonly IChunker chunker;
    private readonly IngestionOptions options;

    public ArticleProcessor(IWikitextCleaner cleaner, IChunker chunker, IOptions<IngestionOptions> options)
    {
        this.cleaner = cleaner;
        this.chunker = chunker;
        this.options = options.Value;
        ProcessingSignature = Hashing.Sha256Hex($"{cleaner.Version}|{chunker.Signature}");
    }

    public string ProcessingSignature { get; }

    public PreparedArticle Prepare(DumpPage page)
    {
        var title = WikiTitles.Normalize(page.Title);
        var chunks = chunker
            .Chunk(cleaner.Clean(page.Text))
            .Select(c => new PreparedChunk(c.SectionHeading, c.Position, c.Text, c.TokenCount, Hashing.Sha256Hex(c.Text)))
            .ToList();

        return new PreparedArticle(
            page.PageId,
            title,
            WikiTitles.ToUrl(options.SourceBaseUrl, title),
            Hashing.Sha256Hex(page.Text),
            ProcessingSignature,
            page.Timestamp,
            chunks
        );
    }
}
