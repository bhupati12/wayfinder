namespace Llm.Core.Models;

public enum ChatRole
{
    System,
    User,
    Assistant,
}

public sealed record ChatMessage(ChatRole Role, string Content);

/// <summary>One chat completion request.</summary>
/// <param name="Messages">Conversation so far, system message first.</param>
/// <param name="MaxOutputTokens">Upper bound on generated tokens; null uses the client's default.</param>
/// <param name="Temperature">Sampling temperature; null uses the client's default.</param>
public sealed record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    int? MaxOutputTokens = null,
    double? Temperature = null
);

/// <summary>Token usage of a completion. Estimated is true when the provider reported none.</summary>
public sealed record ChatUsage(int InputTokens, int OutputTokens, bool Estimated);

/// <summary>A finished, non-streamed completion.</summary>
public sealed record ChatCompletion(string Text, string Model, string? FinishReason, ChatUsage Usage);

/// <summary>
/// One piece of a streamed completion: text as it arrives, then a final update with the finish
/// reason and usage.
/// </summary>
public sealed record ChatStreamUpdate(string? TextDelta, string? FinishReason = null, ChatUsage? Usage = null)
{
    public bool IsFinal => Usage is not null;
}
