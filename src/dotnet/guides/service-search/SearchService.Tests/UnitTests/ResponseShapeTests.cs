using System.Text.Json;
using System.Text.Json.Serialization;
using DomainModels.Guides.Models;

namespace SearchService.Tests.UnitTests;

/// <summary>
/// Snapshots of the public JSON shapes. Renaming or removing a field breaks clients, so a change
/// here must be deliberate: update the snapshot with UPDATE_SNAPSHOTS=1 and consider a new API version.
/// </summary>
public class ResponseShapeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Search_response_shape_matches_the_snapshot() =>
        AssertSnapshot(
            "search-response",
            new SearchResponse
            {
                Query = "ramen",
                Mode = SearchMode.Hybrid,
                K = 1,
                ElapsedMilliseconds = 12,
                Results =
                [
                    new SearchResultItem
                    {
                        ChunkId = 1,
                        ArticleTitle = "Akita",
                        Section = "Eat",
                        Text = "Akita is famous for ramen.",
                        SourceUrl = "https://en.wikivoyage.org/wiki/Akita",
                        Score = 0.0328,
                        VectorRank = 1,
                        KeywordRank = 1,
                    },
                ],
            }
        );

    [Fact]
    public void Ask_response_shape_matches_the_snapshot() =>
        AssertSnapshot(
            "ask-response",
            new AskResponse
            {
                Question = "Where to eat ramen?",
                Answer = "Akita is famous for ramen [1].",
                Answered = true,
                Citations = [new Citation(1, 1, "Akita", "Eat", "https://en.wikivoyage.org/wiki/Akita")],
                Metadata = new AnswerMetadata
                {
                    PromptVersion = "answer-prompt-v1",
                    Model = "qwen2.5:3b",
                    SourcesRetrieved = 6,
                    SourcesUsed = 5,
                    InputTokens = 1800,
                    OutputTokens = 40,
                    RetrievalMilliseconds = 80,
                    GenerationMilliseconds = 9000,
                    TotalMilliseconds = 9100,
                    EstimatedCostUsd = 0,
                },
            }
        );

    private static void AssertSnapshot<T>(string name, T value)
    {
        var actual = JsonSerializer.Serialize(value, Json).ReplaceLineEndings("\n") + "\n";
        var relative = Path.Combine("UnitTests", "Snapshots", name + ".json");

        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(directory!.FullName, "SearchService.Tests.csproj")))
            {
                directory = directory.Parent;
            }

            File.WriteAllText(Path.Combine(directory.FullName, relative), actual);
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, relative);
        Assert.True(File.Exists(path), $"Snapshot {relative} is missing; run the tests with UPDATE_SNAPSHOTS=1.");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }
}
