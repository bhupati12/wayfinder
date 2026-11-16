using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using GuidesImporter.Application.Models;
using GuidesImporter.Application.Services.Interfaces;
using ICSharpCode.SharpZipLib.BZip2;

namespace GuidesImporter.Application.Services;

/// <summary>
/// Streams a MediaWiki XML export. The file is read forward once with an <see cref="XmlReader"/>;
/// only the current &lt;page&gt; element is materialised, so memory stays flat no matter how large
/// the dump is. Files ending in .bz2 are decompressed on the fly.
/// </summary>
public sealed class DumpReader : IDumpReader
{
    public async IAsyncEnumerable<DumpPage> ReadPagesAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1 << 16,
            useAsync: true
        );

        await using var input = path.EndsWith(".bz2", StringComparison.OrdinalIgnoreCase)
            ? new BufferedStream(new BZip2InputStream(file) { IsStreamOwner = false }, 1 << 16)
            : (Stream)file;

        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        using var reader = XmlReader.Create(input, settings);
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "page")
            {
                continue;
            }

            XElement page;
            using (var subtree = reader.ReadSubtree())
            {
                page = await XElement.LoadAsync(subtree, LoadOptions.None, cancellationToken);
            }

            var parsed = Parse(page);
            if (parsed is not null)
            {
                yield return parsed;
            }
        }
    }

    internal static DumpPage? Parse(XElement page)
    {
        var ns = page.Name.Namespace;
        var title = page.Element(ns + "title")?.Value;
        var idText = page.Element(ns + "id")?.Value;
        if (title is null || !long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        _ = int.TryParse(page.Element(ns + "ns")?.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var pageNamespace);
        var redirect = page.Element(ns + "redirect")?.Attribute("title")?.Value;

        var revision = page.Element(ns + "revision");
        var text = revision?.Element(ns + "text")?.Value ?? string.Empty;
        DateTimeOffset? timestamp = DateTimeOffset.TryParse(
            revision?.Element(ns + "timestamp")?.Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsedTimestamp
        )
            ? parsedTimestamp
            : null;

        return new DumpPage(id, title, pageNamespace, redirect, text, timestamp);
    }
}
