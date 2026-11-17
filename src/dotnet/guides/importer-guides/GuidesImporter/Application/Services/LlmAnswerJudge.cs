using System.Text;
using System.Text.RegularExpressions;
using GuidesImporter.Application.Models;
using GuidesImporter.Application.Services.Interfaces;
using Llm.Core.Interfaces;
using Llm.Core.Models;

namespace GuidesImporter.Application.Services;

/// <summary>
/// LLM-as-judge for groundedness: does every claim in the answer appear in the sources it cites?
/// A judge model makes mistakes of its own (especially a small local one, and especially when it
/// is the same model that wrote the answers), which is why a sample is checked by hand.
/// </summary>
public sealed partial class LlmAnswerJudge : IAnswerJudge
{
    public const string PromptVersion = "judge-prompt-v1";

    private const string SystemPrompt = """
        You grade answers written by a travel assistant. You get a question, the assistant's answer, and the sources the answer cites.

        Decide whether the answer is supported: every factual claim in it must be stated in, or directly follow from, the sources. Ignore style and length. An answer that adds facts that are not in the sources is unsupported, even if those facts are true.

        Reply with exactly two lines:
        VERDICT: SUPPORTED or VERDICT: UNSUPPORTED
        REASON: one short sentence
        """;

    private readonly IChatClient chat;

    public LlmAnswerJudge(IChatClient chat)
    {
        this.chat = chat;
    }

    public string Model => chat.Model;

    public async Task<(JudgeVerdict Verdict, string? Reason)> JudgeAsync(
        string question,
        string answer,
        IReadOnlyList<string> sources,
        CancellationToken cancellationToken
    )
    {
        if (sources.Count == 0)
        {
            return (JudgeVerdict.Unsupported, "The answer cites no sources.");
        }

        var user = new StringBuilder();
        user.Append("Question: ").Append(question).Append("\n\nSources:\n");
        for (var i = 0; i < sources.Count; i++)
        {
            user.Append("--- source ").Append(i + 1).Append(" ---\n").Append(sources[i]).Append('\n');
        }

        user.Append("\nAnswer to grade:\n").Append(answer);

        var completion = await chat.CompleteAsync(
            new ChatRequest(
                [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, user.ToString())],
                80,
                0
            ),
            cancellationToken
        );
        return Parse(completion.Text);
    }

    public static (JudgeVerdict Verdict, string? Reason) Parse(string reply)
    {
        var verdict = VerdictRegex().Match(reply);
        var reason = ReasonRegex().Match(reply);
        var parsed =
            !verdict.Success ? JudgeVerdict.Unclear
            : verdict.Groups["v"].Value.Equals("SUPPORTED", StringComparison.OrdinalIgnoreCase) ? JudgeVerdict.Supported
            : JudgeVerdict.Unsupported;
        return (parsed, reason.Success ? reason.Groups["r"].Value.Trim() : null);
    }

    [GeneratedRegex(@"VERDICT\s*:\s*\**\s*(?<v>UNSUPPORTED|SUPPORTED)", RegexOptions.IgnoreCase)]
    private static partial Regex VerdictRegex();

    [GeneratedRegex(@"REASON\s*:\s*(?<r>.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReasonRegex();
}
