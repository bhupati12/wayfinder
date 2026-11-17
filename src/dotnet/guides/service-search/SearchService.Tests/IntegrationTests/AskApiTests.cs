using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DomainModels.Guides.Models;

namespace SearchService.Tests.IntegrationTests;

/// <summary>POST /api/ask and /api/ask/stream end to end, with a fake chat model.</summary>
public sealed class AskApiTests : IClassFixture<SearchApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly SearchApiFactory factory;
    private readonly HttpClient client;

    public AskApiTests(SearchApiFactory factory)
    {
        this.factory = factory;
        factory.Embeddings.Failing = false;
        factory.Chat.Failing = false;
        factory.Chat.Reply = _ => "Akita is known for ramen and kiritanpo [1].";
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Ask_returns_an_answer_with_citations_tokens_and_timings()
    {
        var response = await client.PostAsJsonAsync("/api/ask", new { question = "What food is Akita known for?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AskResponse>(Json))!;
        Assert.True(body.Answered);
        var citation = Assert.Single(body.Citations);
        Assert.Equal(1, citation.Number);
        Assert.StartsWith("https://en.wikivoyage.org/wiki/", citation.SourceUrl);
        Assert.Equal("answer-prompt-v1", body.Metadata.PromptVersion);
        Assert.True(body.Metadata.SourcesUsed > 0);
        Assert.Equal(100, body.Metadata.InputTokens);
        Assert.True(body.Metadata.TotalMilliseconds >= body.Metadata.RetrievalMilliseconds);

        // The top-ranked chunk for this question is the Akita food chunk, sent as source 1.
        var prompt = factory.Chat.Requests[^1].Messages[1].Content;
        Assert.Contains("<source id=\"1\" title=\"Akita\" section=\"Eat\">", prompt);
    }

    [Fact]
    public async Task Off_topic_question_gets_i_dont_know()
    {
        factory.Chat.Reply = _ => "I don't know based on the available guides.";

        var body = (await (await client.PostAsJsonAsync("/api/ask", new { question = "Who won the 1998 football world cup?" })).Content.ReadFromJsonAsync<AskResponse>(Json))!;

        Assert.False(body.Answered);
        Assert.Empty(body.Citations);
    }

    [Fact]
    public async Task Stream_sends_sources_then_tokens_then_done()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask/stream")
        {
            Content = JsonContent.Create(new { question = "What food is Akita known for?" }),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = ParseEvents(await response.Content.ReadAsStringAsync());
        Assert.Equal("sources", events[0].Name);
        Assert.Equal("done", events[^1].Name);
        var tokens = events.Where(e => e.Name == "token").Select(e => JsonDocument.Parse(e.Data).RootElement.GetProperty("text").GetString()).ToList();
        Assert.True(tokens.Count > 1);
        Assert.Equal("Akita is known for ramen and kiritanpo [1].", string.Concat(tokens));

        var done = JsonSerializer.Deserialize<AskResponse>(events[^1].Data, Json)!;
        Assert.Single(done.Citations);
    }

    [Fact]
    public async Task Ask_returns_503_when_the_chat_model_is_down_and_search_still_works()
    {
        factory.Chat.Failing = true;

        var ask = await client.PostAsJsonAsync("/api/ask", new { question = "What food is Akita known for?" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ask.StatusCode);
        Assert.Equal("application/problem+json", ask.Content.Headers.ContentType?.MediaType);

        var stream = await client.PostAsJsonAsync("/api/ask/stream", new { question = "What food is Akita known for?" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, stream.StatusCode);

        var search = await client.GetAsync("/api/search?q=ramen");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"question":"hi"}""")]
    [InlineData("""{"question":"What food is Akita known for?","k":0}""")]
    [InlineData("""{"question":"What food is Akita known for?","k":21}""")]
    public async Task Invalid_ask_requests_get_problem_details(string json)
    {
        var response = await client.PostAsync("/api/ask", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static List<(string Name, string Data)> ParseEvents(string body) =>
        body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block =>
            {
                var lines = block.Split('\n');
                var name = lines.First(l => l.StartsWith("event: ", StringComparison.Ordinal))[7..];
                var data = lines.First(l => l.StartsWith("data: ", StringComparison.Ordinal))[6..];
                return (name, data);
            })
            .ToList();
}
