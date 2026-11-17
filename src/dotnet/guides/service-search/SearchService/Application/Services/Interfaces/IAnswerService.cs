using DomainModels.Guides.Models;

namespace SearchService.Application.Services.Interfaces;

/// <summary>An event of a streamed answer.</summary>
public abstract record AnswerStreamEvent
{
    /// <summary>The numbered sources sent to the model; comes first.</summary>
    public sealed record Sources(IReadOnlyList<Citation> Items) : AnswerStreamEvent;

    /// <summary>Generated text as it arrives.</summary>
    public sealed record Token(string Text) : AnswerStreamEvent;

    /// <summary>The complete answer with citations and metadata; comes last.</summary>
    public sealed record Done(AskResponse Response) : AnswerStreamEvent;
}

public interface IAnswerService
{
    /// <exception cref="Llm.Core.Exceptions.LlmProviderException">The chat model is unavailable.</exception>
    Task<AskResponse> AskAsync(string question, int? k, CancellationToken cancellationToken);

    /// <summary>
    /// Streams the answer. Retrieval and the request to the model happen before the first event,
    /// so a model that is down fails before anything is sent to the client.
    /// </summary>
    IAsyncEnumerable<AnswerStreamEvent> StreamAsync(string question, int? k, CancellationToken cancellationToken);
}
