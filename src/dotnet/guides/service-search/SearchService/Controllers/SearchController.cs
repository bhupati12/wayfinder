using DomainModels.Guides.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SearchService.Application.Models;
using SearchService.Application.Options;
using SearchService.Application.Services.Interfaces;

namespace SearchService.Controllers;

/// <summary>Retrieval over the guide chunks. No business logic here: it validates and delegates.</summary>
[ApiController]
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    private readonly ISearchService search;
    private readonly SearchOptions options;

    public SearchController(ISearchService search, IOptions<SearchOptions> options)
    {
        this.search = search;
        this.options = options.Value;
    }

    /// <summary>Returns the chunks most relevant to <c>q</c>.</summary>
    /// <response code="200">Ranked chunks with their source articles.</response>
    /// <response code="400">Invalid q, mode or k (problem details).</response>
    /// <response code="503">Vector mode and the embedding provider is unavailable (problem details).</response>
    [HttpGet]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<SearchResponse> Get([FromQuery] SearchRequest request, CancellationToken cancellationToken) =>
        search.SearchAsync(request.Query!, request.Mode, request.K ?? options.DefaultK, cancellationToken);
}
