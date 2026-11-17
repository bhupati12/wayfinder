using GuidesImporter.Application.Models;

namespace GuidesImporter.Application.Services.Interfaces;

/// <summary>Calls the SearchService's public API for the evaluation.</summary>
public interface IEvalApiClient
{
    Task<IReadOnlyList<EvalHit>> SearchAsync(string query, string mode, int k, CancellationToken cancellationToken);

    Task<EvalAnswer> AskAsync(string question, CancellationToken cancellationToken);
}
