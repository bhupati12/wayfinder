using System.Net;
using System.Text.Json;
using Llm.Core.Exceptions;
using Llm.Core.Interfaces;
using Llm.Core.Models;
using Llm.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Llm.Infrastructure.Tests.Services;

/// <summary>
/// Exercises the client through the real DI registration, so the resilience pipeline (retries,
/// timeouts) is the one production uses. Only the network is replaced by a stub handler, and
/// retry delays are shortened to a millisecond.
/// </summary>
public class OpenAiCompatibleEmbeddingClientTests
{
    private const int Dimensions = 3;

    [Fact]
    public async Task Retries_429_and_503_then_succeeds()
    {
        var stub = new StubHttpHandler()
            .Then(HttpStatusCode.TooManyRequests, """{"error":"slow down"}""")
            .Then(HttpStatusCode.ServiceUnavailable)
            .Then(HttpStatusCode.OK, StubHttpHandler.EmbeddingsJson(Dimensions, [0, 1]));

        var result = await CreateClient(stub)
            .EmbedAsync(["a", "b"], EmbeddingInputType.Document, CancellationToken.None);

        Assert.Equal(3, stub.Calls);
        Assert.Equal(2, result.Vectors.Count);
        Assert.Equal(10, result.InputTokens);
        Assert.True(result.TokensReportedByProvider);
    }

    [Fact]
    public async Task Retries_network_errors()
    {
        var stub = new StubHttpHandler()
            .ThenThrow(new HttpRequestException("connection refused"))
            .Then(HttpStatusCode.OK, StubHttpHandler.EmbeddingsJson(Dimensions, [0]));

        var result = await CreateClient(stub).EmbedAsync(["a"], EmbeddingInputType.Document, CancellationToken.None);

        Assert.Equal(2, stub.Calls);
        Assert.Single(result.Vectors);
    }

    [Fact]
    public async Task Gives_up_after_the_configured_retries()
    {
        var stub = new StubHttpHandler().Then(HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        var error = await Assert.ThrowsAsync<LlmProviderException>(() =>
            CreateClient(stub, maxRetries: 3).EmbedAsync(["a"], EmbeddingInputType.Document, CancellationToken.None)
        );

        Assert.Equal(4, stub.Calls); // first attempt plus three retries
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.True(error.IsTransient);
        Assert.Contains("boom", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Does_not_retry_client_errors(HttpStatusCode status)
    {
        var stub = new StubHttpHandler().Then(status);

        var error = await Assert.ThrowsAsync<LlmProviderException>(() =>
            CreateClient(stub).EmbedAsync(["a"], EmbeddingInputType.Document, CancellationToken.None)
        );

        Assert.Equal(1, stub.Calls);
        Assert.Equal(status, error.StatusCode);
        Assert.False(error.IsTransient);
    }

    [Fact]
    public async Task Returns_vectors_in_input_order_even_when_the_response_is_not()
    {
        var stub = new StubHttpHandler().Then(HttpStatusCode.OK, StubHttpHandler.EmbeddingsJson(Dimensions, [2, 0, 1]));

        var result = await CreateClient(stub)
            .EmbedAsync(["a", "b", "c"], EmbeddingInputType.Document, CancellationToken.None);

        Assert.Equal([0.5f, 1.5f, 2.5f], result.Vectors.Select(v => v[0]));
    }

    [Fact]
    public async Task Sends_model_prefixed_inputs_and_api_key()
    {
        var stub = new StubHttpHandler().Then(HttpStatusCode.OK, StubHttpHandler.EmbeddingsJson(Dimensions, [0]));

        await CreateClient(stub, apiKey: "test-key")
            .EmbedAsync(["where to eat"], EmbeddingInputType.Query, CancellationToken.None);

        var (request, body) = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://embeddings.test/v1/embeddings", request.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", request.Headers.Authorization!.ToString());

        using var json = JsonDocument.Parse(body);
        Assert.Equal("test-model", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("q: where to eat", json.RootElement.GetProperty("input")[0].GetString());
        Assert.Equal("float", json.RootElement.GetProperty("encoding_format").GetString());
        Assert.False(json.RootElement.TryGetProperty("dimensions", out _));
    }

    [Fact]
    public async Task Rejects_vectors_of_the_wrong_length()
    {
        var stub = new StubHttpHandler().Then(HttpStatusCode.OK, StubHttpHandler.EmbeddingsJson(Dimensions + 1, [0]));

        var error = await Assert.ThrowsAsync<LlmProviderException>(() =>
            CreateClient(stub).EmbedAsync(["a"], EmbeddingInputType.Document, CancellationToken.None)
        );

        Assert.Contains("dimensions", error.Message);
    }

    [Fact]
    public async Task Rejects_a_response_with_too_few_vectors()
    {
        var stub = new StubHttpHandler().Then(HttpStatusCode.OK, StubHttpHandler.EmbeddingsJson(Dimensions, [0]));

        await Assert.ThrowsAsync<LlmProviderException>(() =>
            CreateClient(stub).EmbedAsync(["a", "b"], EmbeddingInputType.Document, CancellationToken.None)
        );
    }

    [Fact]
    public async Task Estimates_tokens_when_the_provider_reports_no_usage()
    {
        var stub = new StubHttpHandler().Then(
            HttpStatusCode.OK,
            StubHttpHandler.EmbeddingsJson(Dimensions, [0], promptTokens: null)
        );

        var result = await CreateClient(stub)
            .EmbedAsync(["abcdefghijklmnop"], EmbeddingInputType.Document, CancellationToken.None);

        Assert.False(result.TokensReportedByProvider);
        Assert.Equal(5, result.InputTokens); // ("d: " + 16 characters + 3) / 4
    }

    [Fact]
    public async Task Empty_input_makes_no_request()
    {
        var stub = new StubHttpHandler();

        var result = await CreateClient(stub).EmbedAsync([], EmbeddingInputType.Document, CancellationToken.None);

        Assert.Empty(result.Vectors);
        Assert.Equal(0, stub.Calls);
    }

    private static IEmbeddingClient CreateClient(StubHttpHandler stub, int maxRetries = 4, string? apiKey = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Embeddings:BaseUrl"] = "http://embeddings.test/v1/",
                    ["Embeddings:Model"] = "test-model",
                    ["Embeddings:Dimensions"] = Dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Embeddings:DocumentPrefix"] = "d: ",
                    ["Embeddings:QueryPrefix"] = "q: ",
                    ["Embeddings:ApiKey"] = apiKey,
                    ["Embeddings:MaxRetries"] = maxRetries.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Embeddings:RetryBaseDelay"] = "00:00:00.001",
                    ["Embeddings:AttemptTimeoutSeconds"] = "5",
                    ["Embeddings:TotalTimeoutSeconds"] = "30",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddEmbeddingClient(configuration).ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider().GetRequiredService<IEmbeddingClient>();
    }
}
