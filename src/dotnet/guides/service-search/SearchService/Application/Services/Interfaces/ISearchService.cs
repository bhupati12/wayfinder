using DomainModels.Guides.Models;

namespace SearchService.Application.Services.Interfaces;

public interface ISearchService
{
    /// <summary>
    /// Runs the search in the requested mode. Hybrid falls back to keyword search, marked as
    /// degraded, when the query cannot be embedded; vector mode throws instead.
    /// </summary>
    /// <exception cref="Llm.Core.Exceptions.LlmProviderException">Vector mode and the embedding provider failed.</exception>
    Task<SearchResponse> SearchAsync(string query, SearchMode mode, int k, CancellationToken cancellationToken);
}
