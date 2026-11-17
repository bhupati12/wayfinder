using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using SearchService.Application.Models;
using SearchService.Application.Options;

namespace SearchService.Application.Validation;

/// <summary>Checks query length and K against the configured limits.</summary>
public static class SearchRequestValidator
{
    public static IEnumerable<ValidationResult> Validate(SearchRequest request, ValidationContext context)
    {
        var options = context.GetService<IOptions<SearchOptions>>()?.Value ?? new SearchOptions();
        var query = request.Query?.Trim() ?? string.Empty;

        if (query.Length < options.MinQueryLength)
        {
            yield return new ValidationResult(
                $"q is required and must be at least {options.MinQueryLength} characters.",
                ["q"]
            );
        }
        else if (query.Length > options.MaxQueryLength)
        {
            yield return new ValidationResult($"q must be at most {options.MaxQueryLength} characters.", ["q"]);
        }

        if (request.K is { } k && (k < 1 || k > options.MaxK))
        {
            yield return new ValidationResult($"k must be between 1 and {options.MaxK}.", ["k"]);
        }
    }
}
