using GuidesImporter.Application.Models;

namespace GuidesImporter.Application.Services.Interfaces;

/// <summary>Streams pages out of a MediaWiki XML dump, one page in memory at a time.</summary>
public interface IDumpReader
{
    /// <param name="path">A .xml or .xml.bz2 dump file.</param>
    IAsyncEnumerable<DumpPage> ReadPagesAsync(string path, CancellationToken cancellationToken);
}
