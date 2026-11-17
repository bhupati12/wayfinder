using System.Net;
using System.Runtime.CompilerServices;
using Llm.Core.Exceptions;
using Llm.Core.Interfaces;
using Llm.Core.Models;

namespace SearchService.Tests;

/// <summary>
/// Chat stand-in: replies with <see cref="Reply"/>, streamed word by word, and records every
/// request so tests can inspect the prompt.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    public string Model => "fake-chat";

    public Func<ChatRequest, string> Reply { get; set; } = _ => "Answer [1].";

    public bool Failing { get; set; }

    public List<ChatRequest> Requests { get; } = [];

    public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        Record(request);
        var text = Reply(request);
        return Task.FromResult(new ChatCompletion(text, Model, "stop", new ChatUsage(100, text.Split(' ').Length, Estimated: false)));
    }

    public async IAsyncEnumerable<ChatStreamUpdate> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Record(request);
        var words = Reply(request).Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            await Task.Yield();
            yield return new ChatStreamUpdate(i == 0 ? words[i] : " " + words[i]);
        }

        yield return new ChatStreamUpdate(null, "stop", new ChatUsage(100, words.Length, Estimated: false));
    }

    private void Record(ChatRequest request)
    {
        if (Failing)
        {
            throw new LlmProviderException("chat model down", HttpStatusCode.ServiceUnavailable);
        }

        Requests.Add(request);
    }
}
