namespace GuidesImporter.Application.Services.Interfaces;

/// <summary>Finds every article below a region in Wikivoyage's IsPartOf breadcrumb hierarchy.</summary>
public interface IRegionResolver
{
    /// <summary>
    /// Reads the dump once and returns the normalised titles of the regions and all their
    /// descendants. Throws when a region is not in the dump.
    /// </summary>
    Task<IReadOnlySet<string>> ResolveAsync(
        string dumpPath,
        IReadOnlyCollection<string> regionRoots,
        CancellationToken cancellationToken
    );
}
