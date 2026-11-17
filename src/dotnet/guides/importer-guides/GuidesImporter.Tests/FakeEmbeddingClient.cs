using System.Security.Cryptography;
using System.Text;
using GuidesDbEntities.Configurations;
using Llm.Core.Interfaces;
using Llm.Core.Models;

namespace GuidesImporter.Tests;

/// <summary>
/// Deterministic stand-in for the embedding provider: the vector is derived from a hash of the
/// text, and tokens are counted as words. <see cref="OnCall"/> can make a given call fail.
/// </summary>
internal sealed class FakeEmbeddingClient : IEmbeddingClient
{
    public FakeEmbeddingClient(string model = "fake-model")
    {
        Model = model;
    }

    public string Model { get; }

    public int Dimensions => GuideChunkConfiguration.EmbeddingDimensions;

    /// <summary>Every input received, per call.</summary>
    public List<IReadOnlyList<string>> Calls { get; } = [];

    /// <summary>Runs before each call with its 1-based number and inputs; throw from it to simulate a failure.</summary>
    public Action<int, IReadOnlyList<string>>? OnCall { get; set; }

    public int InputsEmbedded => Calls.Sum(c => c.Count);

    public Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken
    )
    {
        Calls.Add(inputs);
        OnCall?.Invoke(Calls.Count, inputs);

        var vectors = inputs.Select(Vector).ToList();
        var tokens = inputs.Sum(i => i.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        return Task.FromResult(new EmbeddingResult(vectors, tokens, TokensReportedByProvider: true));
    }

    private float[] Vector(string text)
    {
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var vector = new float[Dimensions];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (seed[i % seed.Length] + i) / 255f;
        }

        return vector;
    }
}
