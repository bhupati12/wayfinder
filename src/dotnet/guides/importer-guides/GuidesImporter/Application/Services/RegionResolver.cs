using System.Text.RegularExpressions;
using GuidesImporter.Application.Helpers;
using GuidesImporter.Application.Services.Interfaces;

namespace GuidesImporter.Application.Services;

/// <summary>
/// Wikivoyage articles name their parent region with {{IsPartOf|Parent}}. This reads the dump
/// once, builds the parent-to-children map (a few hundred kilobytes of titles) and walks it from
/// the requested root. Redirects are followed so a parent given by an old name still resolves.
/// </summary>
public sealed partial class RegionResolver : IRegionResolver
{
    private readonly IDumpReader dumpReader;
    private readonly ILogger<RegionResolver> logger;

    public RegionResolver(IDumpReader dumpReader, ILogger<RegionResolver> logger)
    {
        this.dumpReader = dumpReader;
        this.logger = logger;
    }

    public async Task<IReadOnlySet<string>> ResolveAsync(
        string dumpPath,
        IReadOnlyCollection<string> regionRoots,
        CancellationToken cancellationToken
    )
    {
        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var redirects = new Dictionary<string, string>(StringComparer.Ordinal);
        var articles = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var page in dumpReader.ReadPagesAsync(dumpPath, cancellationToken))
        {
            if (page.Namespace != 0)
            {
                continue;
            }

            var title = WikiTitles.Normalize(page.Title);
            if (page.RedirectTarget is not null)
            {
                redirects[title] = WikiTitles.Normalize(page.RedirectTarget);
                continue;
            }

            articles.Add(title);
            var match = IsPartOfRegex().Match(page.Text);
            if (match.Success)
            {
                parentOf[title] = WikiTitles.Normalize(match.Groups["parent"].Value);
            }
        }

        var roots = regionRoots.Select(r => Resolve(WikiTitles.Normalize(r), redirects)).ToList();
        var missing = roots.Where(r => !articles.Contains(r)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Regions not found in the dump: {string.Join(", ", missing)}");
        }

        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (child, parent) in parentOf)
        {
            var resolvedParent = Resolve(parent, redirects);
            if (!children.TryGetValue(resolvedParent, out var list))
            {
                children[resolvedParent] = list = [];
            }

            list.Add(child);
        }

        var region = new HashSet<string>(roots, StringComparer.Ordinal);
        var queue = new Queue<string>(roots);
        while (queue.TryDequeue(out var current))
        {
            if (!children.TryGetValue(current, out var list))
            {
                continue;
            }

            foreach (var child in list.Where(region.Add))
            {
                queue.Enqueue(child);
            }
        }

        logger.LogInformation(
            "Regions {Regions} have {Count} articles (of {Total} in the dump)",
            string.Join(", ", roots),
            region.Count,
            articles.Count
        );
        return region;
    }

    private static string Resolve(string title, Dictionary<string, string> redirects)
    {
        // Follow at most a few hops; redirect chains are rare and loops are possible.
        for (var hop = 0; hop < 5 && redirects.TryGetValue(title, out var target); hop++)
        {
            title = target;
        }

        return title;
    }

    [GeneratedRegex(@"\{\{\s*IsPartOf\s*\|\s*(?<parent>[^|}]+?)\s*(\||\}\})", RegexOptions.IgnoreCase)]
    private static partial Regex IsPartOfRegex();
}
