using GuidesImporter.Application.Models;

namespace GuidesImporter.Application.Services.Interfaces;

/// <summary>Cleans, chunks and hashes one dump page.</summary>
public interface IArticleProcessor
{
    /// <summary>Hash of the cleaner version and chunking settings, stored on every article.</summary>
    string ProcessingSignature { get; }

    PreparedArticle Prepare(DumpPage page);
}
