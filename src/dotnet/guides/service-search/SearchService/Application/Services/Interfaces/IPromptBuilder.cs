using Llm.Core.Models;
using SearchService.Application.Models;

namespace SearchService.Application.Services.Interfaces;

/// <summary>Builds the grounded-answer prompt. The template lives in one place and is versioned.</summary>
public interface IPromptBuilder
{
    /// <summary>Changes whenever the template's wording changes, and is reported with every answer.</summary>
    string Version { get; }

    /// <summary>
    /// Hash of the template's fixed text. Part of the answer cache key, so editing the wording
    /// without bumping <see cref="Version"/> cannot serve answers made with the old wording.
    /// </summary>
    string TemplateHash { get; }

    /// <summary>The reply the prompt tells the model to give when the sources do not cover the question.</summary>
    string NoAnswerText { get; }

    IReadOnlyList<ChatMessage> Build(string question, IReadOnlyList<PromptSource> sources);

    /// <summary>The source block exactly as it appears in the prompt, for budgeting.</summary>
    string FormatSource(PromptSource source);
}
