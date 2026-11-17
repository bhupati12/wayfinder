using GuidesImporter.Application.Models;

namespace GuidesImporter.Application.Services.Interfaces;

/// <summary>Asks a model whether an answer is supported by its sources.</summary>
public interface IAnswerJudge
{
    string Model { get; }

    Task<(JudgeVerdict Verdict, string? Reason)> JudgeAsync(
        string question,
        string answer,
        IReadOnlyList<string> sources,
        CancellationToken cancellationToken
    );
}
