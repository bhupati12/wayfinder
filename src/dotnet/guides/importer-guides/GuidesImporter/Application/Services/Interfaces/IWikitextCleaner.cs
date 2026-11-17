namespace GuidesImporter.Application.Services.Interfaces;

/// <summary>Turns wikitext into plain text.</summary>
public interface IWikitextCleaner
{
    /// <summary>
    /// Identifies the cleaning rules. Bump it when the output changes so the next ingest rebuilds
    /// chunks for articles whose text did not change.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Returns plain text. Section headings are kept on their own line as "== Heading ==" (two to
    /// six equals signs, matching the level) so the chunker can split on them. Templates, tables,
    /// references, files and markup are removed; Wikivoyage listings become one line each.
    /// </summary>
    string Clean(string wikitext);
}
