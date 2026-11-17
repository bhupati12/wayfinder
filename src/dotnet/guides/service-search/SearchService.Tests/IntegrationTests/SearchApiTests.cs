using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DomainModels.Guides.Models;

namespace SearchService.Tests.IntegrationTests;

/// <summary>GET /api/search end to end: HTTP, validation, SQL and fusion against PostgreSQL.</summary>
public sealed class SearchApiTests : IClassFixture<SearchApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SearchApiFactory factory;
    private readonly HttpClient client;

    public SearchApiTests(SearchApiFactory factory)
    {
        this.factory = factory;
        factory.Embeddings.Failing = false;
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Keyword_search_ranks_the_chunk_with_the_word_first()
    {
        var response = await SearchAsync("q=ramen&mode=keyword");

        Assert.Equal(SearchMode.Keyword, response.Mode);
        var top = response.Results[0];
        Assert.Equal(await factory.ChunkIdAsync(0), top.ChunkId);
        Assert.Equal(1, top.KeywordRank);
        Assert.Null(top.VectorRank);
        Assert.Equal("Akita", top.ArticleTitle);
        Assert.Equal("Eat", top.Section);
        Assert.Equal("https://en.wikivoyage.org/wiki/Akita", top.SourceUrl);
        Assert.Contains("ramen", top.Text);
        Assert.Contains("Wikivoyage", response.Attribution);
    }

    [Fact]
    public async Task Keyword_search_stems_words_and_ors_them()
    {
        // "dinner" matches "dinners"; "beaches" matches "beach"-stemmed text; either word is enough.
        var response = await SearchAsync("q=dinner%20beaches&mode=keyword");

        var ids = response.Results.Select(r => r.ChunkId).ToList();
        Assert.Contains(await factory.ChunkIdAsync(5), ids);
        Assert.Contains(await factory.ChunkIdAsync(2), ids);
    }

    [Fact]
    public async Task Keyword_search_uses_all_word_matches_when_there_are_enough()
    {
        // Only the Bangkok chunk has both "noodle" and "night"; with k=1 that is enough.
        var response = await SearchAsync("q=noodle%20night&mode=keyword&k=1");

        Assert.Equal([await factory.ChunkIdAsync(4)], response.Results.Select(r => r.ChunkId));
    }

    [Fact]
    public async Task Keyword_search_falls_back_to_any_word_when_too_few_chunks_have_every_word()
    {
        // k=2 needs two all-word matches and there is one, so chunks with either word count.
        var response = await SearchAsync("q=noodle%20night&mode=keyword&k=2");

        Assert.Equal(
            [await factory.ChunkIdAsync(4), await factory.ChunkIdAsync(0)],
            response.Results.Select(r => r.ChunkId)
        );
    }

    [Theory]
    [InlineData("Try bún chả at a street stall.", "bun cha")]
    [InlineData("The Tōkaidō Shinkansen stops at Kyoto.", "Tokaido shinkansen")]
    [InlineData("Order a bowl of pho for breakfast.", "phở")]
    public async Task Keyword_search_ignores_diacritics(string text, string query)
    {
        Assert.True(await factory.KeywordMatchesAsync(text, query));
    }

    [Fact]
    public async Task Vector_search_finds_meaning_that_shares_no_words_with_the_query()
    {
        // No chunk contains "cuisine", so keyword search finds nothing; vector search finds food.
        var keyword = await SearchAsync("q=cuisine&mode=keyword");
        Assert.Empty(keyword.Results);

        var vector = await SearchAsync("q=cuisine&mode=vector&k=3");
        var foodChunks = new[]
        {
            await factory.ChunkIdAsync(0),
            await factory.ChunkIdAsync(4),
            await factory.ChunkIdAsync(5),
        };
        Assert.Equal(3, vector.Results.Count);
        Assert.All(vector.Results, r => Assert.Contains(r.ChunkId, foodChunks));
        Assert.True(vector.Results[0].Score >= vector.Results[1].Score);
        Assert.Equal([1, 2, 3], vector.Results.Select(r => r.VectorRank!.Value));
    }

    [Fact]
    public async Task Hybrid_search_ranks_a_chunk_found_by_both_methods_first()
    {
        var response = await SearchAsync("q=Akita%20train");

        Assert.Equal(SearchMode.Hybrid, response.Mode);
        var top = response.Results[0];
        Assert.Equal(await factory.ChunkIdAsync(1), top.ChunkId);
        Assert.NotNull(top.VectorRank);
        Assert.NotNull(top.KeywordRank);
        Assert.False(response.Degraded);
    }

    [Fact]
    public async Task K_limits_the_number_of_results()
    {
        var response = await SearchAsync("q=temple%20food%20beach%20train&mode=vector&k=2");

        Assert.Equal(2, response.K);
        Assert.Equal(2, response.Results.Count);
    }

    [Theory]
    [InlineData("", "q")]
    [InlineData("q=", "q")]
    [InlineData("q=a", "q")]
    [InlineData("q=ramen&k=0", "k")]
    [InlineData("q=ramen&k=51", "k")]
    [InlineData("q=ramen&mode=telepathy", "mode")]
    public async Task Invalid_requests_get_problem_details(string queryString, string field)
    {
        var response = await client.GetAsync($"/api/v1/search?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => e.Name.Equals(field, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Overlong_query_is_rejected()
    {
        var response = await client.GetAsync($"/api/v1/search?q={new string('x', 501)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hybrid_degrades_to_keyword_search_when_the_embedding_provider_is_down()
    {
        factory.Embeddings.Failing = true;

        var response = await SearchAsync("q=ramen");

        Assert.True(response.Degraded);
        Assert.NotNull(response.DegradedReason);
        Assert.Equal(await factory.ChunkIdAsync(0), response.Results[0].ChunkId);
        Assert.All(response.Results, r => Assert.Null(r.VectorRank));
    }

    [Fact]
    public async Task Vector_search_returns_503_when_the_embedding_provider_is_down()
    {
        factory.Embeddings.Failing = true;

        var response = await client.GetAsync("/api/v1/search?q=ramen&mode=vector");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.Contains("Retry-After"));
        Assert.DoesNotContain("   at ", await response.Content.ReadAsStringAsync()); // no stack trace
    }

    private async Task<SearchResponse> SearchAsync(string queryString)
    {
        var response = await client.GetAsync($"/api/v1/search?{queryString}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(Json))!;
    }
}
