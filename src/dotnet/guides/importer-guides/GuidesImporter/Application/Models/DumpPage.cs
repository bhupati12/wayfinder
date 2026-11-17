namespace GuidesImporter.Application.Models;

/// <summary>One &lt;page&gt; element of a MediaWiki XML dump.</summary>
/// <param name="PageId">MediaWiki page id.</param>
/// <param name="Title">Page title as written in the dump.</param>
/// <param name="Namespace">0 for articles; other namespaces are talk, user, template and so on.</param>
/// <param name="RedirectTarget">Target title when the page is a redirect, otherwise null.</param>
/// <param name="Text">Wikitext of the latest revision.</param>
/// <param name="Timestamp">Timestamp of the latest revision.</param>
public sealed record DumpPage(
    long PageId,
    string Title,
    int Namespace,
    string? RedirectTarget,
    string Text,
    DateTimeOffset? Timestamp
)
{
    public bool IsArticle => Namespace == 0 && RedirectTarget is null;
}
