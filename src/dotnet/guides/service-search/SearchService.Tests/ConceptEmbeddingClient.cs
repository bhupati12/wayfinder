using System.Net;
using System.Text.RegularExpressions;
using GuidesDbEntities.Configurations;
using Llm.Core.Exceptions;
using Llm.Core.Interfaces;
using Llm.Core.Models;

namespace SearchService.Tests;

/// <summary>
/// A tiny "semantic" embedding for tests: one dimension per concept (food, transport, beach,
/// temple) and per place, counting the words that belong to it. "cuisine" and "ramen" land in
/// the same dimension although they share no letters, which is the point of vector search.
/// </summary>
internal sealed partial class ConceptEmbeddingClient : IEmbeddingClient
{
    private static readonly string[][] Concepts =
    [
        [
            "ramen",
            "noodle",
            "noodles",
            "food",
            "eat",
            "restaurant",
            "restaurants",
            "kaiseki",
            "soup",
            "dinner",
            "dinners",
            "cuisine",
            "dish",
            "kiritanpo",
            "tofu",
            "street",
        ],
        ["train", "shinkansen", "rail", "bus", "flight", "transport", "travel"],
        ["beach", "beaches", "snorkel", "dive", "sea", "swim", "swimming", "coast"],
        ["temple", "temples", "shrine", "castle", "pagoda", "gates"],
        ["akita"],
        ["kyoto"],
        ["okinawa"],
        ["bangkok"],
    ];

    public string Model => "concept-test-model";

    public int Dimensions => GuideChunkConfiguration.EmbeddingDimensions;

    /// <summary>When true every call fails like an unreachable provider.</summary>
    public bool Failing { get; set; }

    public Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken
    )
    {
        if (Failing)
        {
            throw new LlmProviderException("provider down", HttpStatusCode.ServiceUnavailable);
        }

        return Task.FromResult(
            new EmbeddingResult(inputs.Select(Embed).ToList(), inputs.Count, TokensReportedByProvider: true)
        );
    }

    public float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        vector[^1] = 0.01f; // never all zeros: cosine distance of a zero vector is undefined

        foreach (Match word in WordRegex().Matches(text.ToLowerInvariant()))
        {
            for (var c = 0; c < Concepts.Length; c++)
            {
                if (Concepts[c].Contains(word.Value))
                {
                    vector[c] += 1;
                }
            }
        }

        return vector;
    }

    [GeneratedRegex(@"[a-z]+")]
    private static partial Regex WordRegex();
}
