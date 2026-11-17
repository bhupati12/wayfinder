namespace SearchService.Application.Models;

/// <summary>A retrieved chunk as it is shown to the model, with its citation number.</summary>
public sealed record PromptSource(
    int Number,
    long ChunkId,
    string ArticleTitle,
    string Section,
    string SourceUrl,
    string Text
);
