using DomainModels.Guides.Models;
using GuidesDbEntities.DAL;
using GuidesDbEntities.Models;
using Llm.Core.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pgvector;
using Testcontainers.PostgreSql;

namespace SearchService.Tests.IntegrationTests;

/// <summary>
/// Hosts the real SearchService in memory against a pgvector container seeded with a small
/// fixed dataset. Only the embedding provider is replaced, by <see cref="ConceptEmbeddingClient"/>.
/// </summary>
public sealed class SearchApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>(article, section, text). Chunk ids follow this order, starting at 1.</summary>
    public static readonly (string Article, string Section, string Text)[] Dataset =
    [
        ("Akita", "Eat", "Akita is famous for kiritanpo and ramen. Try the noodle shops near the station."),
        ("Akita", "Get in", "The Akita Shinkansen runs from Tokyo. The train takes about four hours."),
        ("Okinawa", "Do", "Snorkel and dive at the beaches; the sea is warm and clear all year."),
        ("Kyoto", "See", "Kinkaku-ji is a golden temple. Fushimi Inari shrine has thousands of red gates."),
        ("Bangkok", "Eat", "Street food stalls sell pad thai and noodle soup late into the night."),
        ("Kyoto", "Eat", "Kaiseki dinners are expensive. Tofu restaurants cluster near the temples."),
    ];

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    internal ConceptEmbeddingClient Embeddings { get; } = new();

    internal FakeChatClient Chat { get; } = new();

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var articles = new Dictionary<string, GuideArticle>();
        foreach (var (title, section, text) in Dataset)
        {
            if (!articles.TryGetValue(title, out var article))
            {
                article = new GuideArticle
                {
                    PageId = articles.Count + 1,
                    Title = title,
                    Url = $"https://en.wikivoyage.org/wiki/{title}",
                    RawTextHash = new string('a', 64),
                    ProcessingSignature = new string('b', 64),
                    LastIngestedAt = DateTimeOffset.UtcNow,
                };
                articles[title] = article;
                db.Articles.Add(article);
            }

            var hash = new string('c', 64);
            article.Chunks.Add(
                new GuideChunk
                {
                    SectionHeading = section,
                    Position = article.Chunks.Count,
                    Text = text,
                    TokenCount = text.Split(' ').Length,
                    ContentHash = hash,
                    Embedding = new Vector(Embeddings.Embed(ChunkEmbeddingText.Build(title, section, text))),
                    EmbeddingModel = Embeddings.Model,
                    EmbeddedContentHash = hash,
                    EmbeddedAt = DateTimeOffset.UtcNow,
                }
            );
        }

        // Save in dataset order so chunk ids are 1..n in the order above.
        await db.SaveChangesAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await container.DisposeAsync();
    }

    /// <summary>Chunk id of a dataset row, looked up by its text.</summary>
    public async Task<long> ChunkIdAsync(int datasetIndex)
    {
        await using var db = CreateContext();
        var text = Dataset[datasetIndex].Text;
        return await db.Chunks.Where(c => c.Text == text).Select(c => c.Id).SingleAsync();
    }

    public const string ApiKey = "test-api-key-0123456789";

    /// <summary>A client that sends the test API key.</summary>
    public HttpClient CreateAuthorizedClient(WebApplicationFactory<Program>? host = null)
    {
        var client = (host ?? this).CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Guides", container.GetConnectionString());
        builder.UseSetting("Auth:ApiKeys:0:Name", "test-client");
        builder.UseSetting("Auth:ApiKeys:0:Key", ApiKey);
        builder.UseSetting("RateLimiting:AskPermitsPerMinute", "10000");
        builder.UseSetting("RateLimiting:SearchPermitsPerMinute", "100000");
        builder.UseSetting("Caching:Enabled", "false");
        builder.UseSetting("CostCap:DailyTokenCap", "0");
        builder.UseSetting("CostCap:DailyCostCapUsd", "0");
        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IEmbeddingClient>(Embeddings));
            services.Replace(ServiceDescriptor.Singleton<IChatClient>(Chat));
        });
    }

    private GuidesContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GuidesContext>();
        GuidesDbServiceCollectionExtensions.Configure(options, container.GetConnectionString());
        return new GuidesContext(options.Options);
    }
}
