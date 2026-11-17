using System.ComponentModel.DataAnnotations;
using DomainModels.Guides.Models;
using Microsoft.AspNetCore.Mvc;
using SearchService.Application.Validation;

namespace SearchService.Application.Models;

/// <summary>Query string of GET /api/search. Invalid requests get a 400 problem-details response.</summary>
public sealed class SearchRequest : IValidatableObject
{
    /// <summary>The question or keywords.</summary>
    [FromQuery(Name = "q")]
    public string? Query { get; set; }

    [FromQuery(Name = "mode")]
    public SearchMode Mode { get; set; } = SearchMode.Hybrid;

    /// <summary>Number of results; the configured default when omitted.</summary>
    [FromQuery(Name = "k")]
    public int? K { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SearchRequestValidator.Validate(this, validationContext);
}
