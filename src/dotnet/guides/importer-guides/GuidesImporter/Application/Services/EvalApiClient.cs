using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuidesImporter.Application.Models;
using GuidesImporter.Application.Options;
using GuidesImporter.Application.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace GuidesImporter.Application.Services;

/// <summary>Typed HttpClient for the SearchService API, using only the fields the evaluation needs.</summary>
public sealed class EvalApiClient : IEvalApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient http;
    private readonly EvalOptions options;

    public EvalApiClient(HttpClient http, IOptions<EvalOptions> options)
    {
        this.http = http;
        this.options = options.Value;
    }

    public async Task<IReadOnlyList<EvalHit>> SearchAsync(
        string query,
        string mode,
        int k,
        CancellationToken cancellationToken
    )
    {
        var url = $"{options.SearchPath}?q={Uri.EscapeDataString(query)}&mode={mode}&k={k}";
        using var response = await http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<SearchBody>(Json, cancellationToken)
            ?? throw new InvalidOperationException("Empty search response.");
        return body.Results.Select(r => new EvalHit(r.ChunkId, r.ArticleTitle, r.Section)).ToList();
    }

    public async Task<EvalAnswer> AskAsync(string question, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(options.AskPath, new { question }, Json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<AskBody>(Json, cancellationToken)
            ?? throw new InvalidOperationException("Empty ask response.");
        return new EvalAnswer(
            body.Answer,
            body.Answered,
            body.Citations.Select(c => c.ChunkId).ToList(),
            body.Metadata.InputTokens,
            body.Metadata.OutputTokens,
            body.Metadata.TotalMilliseconds
        );
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"{(int)response.StatusCode} from {response.RequestMessage?.RequestUri}: {text[..Math.Min(text.Length, 300)]}"
            );
        }
    }

    private sealed record SearchBody(List<SearchHit> Results);

    private sealed record SearchHit(long ChunkId, string ArticleTitle, string Section);

    private sealed record AskBody(string Answer, bool Answered, List<CitationBody> Citations, MetadataBody Metadata);

    private sealed record CitationBody(long ChunkId);

    private sealed record MetadataBody(int InputTokens, int OutputTokens, long TotalMilliseconds);
}
