using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DomainModels.Guides.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SearchService.Application.Services.Interfaces;

namespace SearchService.Tests.IntegrationTests;

/// <summary>Phase 6 behaviour: authentication, rate limits, caching, cost cap, errors, health, OpenAPI.</summary>
public sealed class HardeningTests : IClassFixture<SearchApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly object Question = new { question = "What food is Akita known for?" };

    private readonly SearchApiFactory factory;

    public HardeningTests(SearchApiFactory factory)
    {
        this.factory = factory;
        factory.Embeddings.Failing = false;
        factory.Chat.Failing = false;
        factory.Chat.Reply = _ => "Akita is known for ramen [1].";
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key-0123456789")]
    public async Task Ask_requires_a_valid_api_key(string? key)
    {
        var client = factory.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", key);
        }

        var response = await client.PostAsJsonAsync("/api/v1/ask", Question);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Search_needs_no_api_key()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/search?q=ramen");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unreachable_database_is_a_503_with_retry_after()
    {
        // Port 1 on the loopback refuses connections, like a stopped PostgreSQL.
        using var host = factory.WithWebHostBuilder(b =>
            b.UseSetting(
                "ConnectionStrings:Guides",
                "Host=127.0.0.1;Port=1;Database=wayfinder;Username=wayfinder;Password=unused"
            )
        );

        var response = await host.CreateClient().GetAsync("/api/v1/search?q=ramen&mode=keyword");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.Contains("Retry-After"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("database is unavailable", body);
        Assert.DoesNotContain("127.0.0.1", body);
    }

    [Fact]
    public async Task Ask_is_rate_limited_per_key()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AskPermitsPerMinute", "2"));
        var client = factory.CreateAuthorizedClient(host);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/ask", Question)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/ask", Question)).StatusCode);
        var third = await client.PostAsJsonAsync("/api/v1/ask", Question);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.True(third.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Repeated_question_is_served_from_the_cache()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Caching:Enabled", "true"));
        var client = factory.CreateAuthorizedClient(host);
        var calls = factory.Chat.Requests.Count;

        var first = (
            await (
                await client.PostAsJsonAsync("/api/v1/ask", new { question = "What food is Akita known for?" })
            ).Content.ReadFromJsonAsync<AskResponse>(Json)
        )!;
        var second = (
            await (
                await client.PostAsJsonAsync("/api/v1/ask", new { question = "  what food is AKITA known for " })
            ).Content.ReadFromJsonAsync<AskResponse>(Json)
        )!;

        Assert.Equal(calls + 1, factory.Chat.Requests.Count); // the normalised repeat never reached the model
        Assert.False(first.Metadata.Cached);
        Assert.True(second.Metadata.Cached);
        Assert.Equal(first.Answer, second.Answer);
        Assert.Equal(0, second.Metadata.InputTokens);
    }

    [Fact]
    public async Task Daily_token_cap_switches_ask_off_but_not_search()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("CostCap:DailyTokenCap", "50"));
        var client = factory.CreateAuthorizedClient(host);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/ask", Question)).StatusCode); // uses 100+ tokens
        var capped = await client.PostAsJsonAsync("/api/v1/ask", Question);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, capped.StatusCode);
        Assert.True(capped.Headers.Contains("Retry-After"));
        Assert.Contains("paused for today", await capped.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/search?q=ramen")).StatusCode);
    }

    [Fact]
    public async Task Correlation_id_is_echoed_and_put_in_problem_details()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/search?q=a");
        request.Headers.Add("X-Correlation-Id", "test-correlation-42");

        var response = await client.SendAsync(request);

        Assert.Equal("test-correlation-42", response.Headers.GetValues("X-Correlation-Id").Single());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("test-correlation-42", body.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Unexpected_errors_are_a_500_problem_without_internals()
    {
        using var host = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Scoped<ISearchService, ThrowingSearch>()))
        );

        var response = await host.CreateClient().GetAsync("/api/v1/search?q=ramen");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret connection detail", body);
        Assert.DoesNotContain("   at ", body);
        Assert.Contains("correlationId", body);
    }

    [Fact]
    public async Task Oversized_ask_body_is_rejected()
    {
        var client = factory.CreateAuthorizedClient();
        var json = $$"""{"question":"{{new string('x', 20_000)}}"}""";

        var response = await client.PostAsync(
            "/api/v1/ask",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest });
    }

    [Theory]
    [InlineData("/healthz/live")]
    [InlineData("/healthz/ready")]
    [InlineData("/healthz")]
    public async Task Health_endpoints_report_healthy(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_document_lists_the_versioned_routes()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/v1/search", body);
        Assert.Contains("/api/v1/ask", body);
        Assert.Contains("/api/v1/ask/stream", body);
    }

    [Fact]
    public async Task OpenApi_document_asks_for_the_api_key_on_ask_only()
    {
        using var document = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());

        Assert.True(paths.GetProperty("/api/v1/ask").GetProperty("post").TryGetProperty("security", out var ask));
        Assert.True(ask[0].TryGetProperty("ApiKey", out _));
        Assert.True(paths.GetProperty("/api/v1/ask/stream").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/api/v1/search").GetProperty("get").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task Swagger_ui_is_off_unless_switched_on()
    {
        var off = await factory.CreateClient().GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.NotFound, off.StatusCode);

        using var host = factory.WithWebHostBuilder(b => b.UseSetting("ApiDocs:SwaggerUi", "true"));
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var page = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("swagger-ui", await page.Content.ReadAsStringAsync());

        var root = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("/swagger", root.Headers.Location?.OriginalString);
    }

    private sealed class ThrowingSearch : ISearchService
    {
        public Task<SearchResponse> SearchAsync(
            string query,
            SearchMode mode,
            int k,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("secret connection detail");
    }
}
