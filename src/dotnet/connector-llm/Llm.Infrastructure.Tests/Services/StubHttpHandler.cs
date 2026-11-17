using System.Net;
using System.Text;

namespace Llm.Infrastructure.Tests.Services;

/// <summary>
/// Returns queued responses in order (the last one repeats) and records every request, so tests
/// can count attempts and inspect what was sent.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> responses = new();
    private Func<HttpResponseMessage>? last;

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public int Calls => Requests.Count;

    public StubHttpHandler Then(HttpStatusCode status, string body = "{}")
    {
        responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        return this;
    }

    public StubHttpHandler ThenThrow(Exception exception)
    {
        responses.Enqueue(() => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));

        if (responses.TryDequeue(out var next))
        {
            last = next;
        }

        return (last ?? throw new InvalidOperationException("No response queued."))();
    }

    /// <summary>An OpenAI-style embeddings response with one vector per value of <paramref name="indexes"/>.</summary>
    public static string EmbeddingsJson(int dimensions, int[] indexes, int? promptTokens = 10)
    {
        var data = string.Join(
            ',',
            indexes.Select(i => $"{{\"object\":\"embedding\",\"index\":{i},\"embedding\":[{string.Join(',', Enumerable.Repeat($"{i}.5", dimensions))}]}}")
        );
        var usage = promptTokens is null ? "" : $",\"usage\":{{\"prompt_tokens\":{promptTokens},\"total_tokens\":{promptTokens}}}";
        return $"{{\"object\":\"list\",\"model\":\"test-model\",\"data\":[{data}]{usage}}}";
    }
}
