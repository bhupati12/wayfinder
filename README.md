# Wayfinder

A question-answering API over public travel guides. Ask a question, get an answer that cites the
guide passages it came from (retrieval-augmented generation).

Status: Phase 2 done (guide articles ingested, chunked and embedded). The build plan is in
[docs/BUILD_PLAN.md](docs/BUILD_PLAN.md).

## Run locally

Start PostgreSQL (with pgvector) and Redis:

```bash
docker compose up -d
```

Build everything:

```bash
dotnet build src/dotnet/Wayfinder.sln
```

Run the search API, then open `/healthz`:

```bash
dotnet run --project src/dotnet/guides/service-search/SearchService
```

### Load the guides

1. Download the English Wikivoyage dump (about 130 MB) into `data/`, which git ignores:

   ```bash
   curl -o data/enwikivoyage-latest-pages-articles.xml.bz2 https://dumps.wikimedia.org/enwikivoyage/latest/enwikivoyage-latest-pages-articles.xml.bz2
   ```

2. Give the importer the database connection string. It is kept in user secrets, not in
   `appsettings.json`; the password is the local-only one from `docker-compose.yml`:

   ```bash
   dotnet user-secrets set "ConnectionStrings:Guides" "Host=localhost;Port=5432;Database=wayfinder;Username=wayfinder;Password=wayfinder_local_only" --project src/dotnet/guides/importer-guides/GuidesImporter
   ```

   Outside development, set the `ConnectionStrings__Guides` environment variable instead.

3. Create the tables, then ingest:

   ```bash
   dotnet run --project src/dotnet/guides/importer-guides/GuidesImporter -- migrateDatabase
   ```

   ```bash
   dotnet run --project src/dotnet/guides/importer-guides/GuidesImporter -- ingestGuides
   ```

   The first run takes about three minutes. Running it again skips every unchanged article.
   Each run is recorded in the `ingestion_runs` table.

Ingestion settings live in the `Ingestion` section of
`src/dotnet/guides/importer-guides/GuidesImporter/appsettings*.json`. Development ingests the East
Asia and Southeast Asia regions (about 3,900 articles); clear `Regions` to ingest everything.
Any setting can be overridden with an environment variable, for example
`Ingestion__ArticleLimit=100`.

### Embed the chunks

Embeddings come from a free local model, `nomic-embed-text` (768 dimensions), served by
[Ollama](https://ollama.com/). No API key or account is needed and no text leaves the machine.

1. Install Ollama and pull the model:

   ```bash
   winget install --id Ollama.Ollama --exact
   ```

   ```bash
   ollama pull nomic-embed-text
   ```

2. Start the server. On a laptop with an integrated AMD or Intel GPU, enabling it roughly doubles
   throughput (Ollama skips integrated GPUs by default):

   ```bash
   OLLAMA_IGPU_ENABLE=1 ollama serve
   ```

3. Run the job:

   ```bash
   dotnet run --project src/dotnet/guides/importer-guides/GuidesImporter -- embedChunks
   ```

   It embeds every chunk that has no embedding, a stale one, or one from another model, and saves
   each batch as it goes, so it can be stopped with Ctrl+C and resumed. Each run is recorded in the
   `embedding_runs` table with its token count and estimated cost. Try a few first with
   `EmbedChunks__MaxChunks=64`.

The client speaks the OpenAI `/v1/embeddings` API, so a hosted provider is a configuration change
in the `Embeddings` section (`BaseUrl`, `Model`, `CostPerMillionTokens`, and the API key in user
secrets as `Embeddings:ApiKey`). A model with a different vector length also needs a migration that
changes the `guide_chunks.embedding` column.

### Tests

```bash
dotnet test src/dotnet/Wayfinder.sln
```

Integration tests start PostgreSQL in a container with Testcontainers, so Docker must be running.

## Layout

| Path | Purpose |
|---|---|
| `src/dotnet/guides/service-search/SearchService` | HTTP API: search and ask |
| `src/dotnet/guides/importer-guides/GuidesImporter` | Batch jobs: migrate, ingest, embed, evaluate |
| `src/dotnet/guides/db-entities-guides/GuidesDbEntities` | EF Core context, entities, migrations |
| `src/dotnet/domains-shared/guides/DomainModels.Guides` | DTOs shared between services |
| `src/dotnet/connector-llm` | LLM interfaces (`Llm.Core`) and provider clients (`Llm.Infrastructure`) |
| `helm/` | Deployment values per service |
| `eval/` | Evaluation question sets |
| `docs/` | Build plan, evaluation results |

## Data and attribution

Guide content comes from [Wikivoyage](https://www.wikivoyage.org/), licensed
[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). Every stored article keeps the URL
of its source page, and answers link back to the source articles. The test fixtures under
`GuidesImporter.Tests/Fixtures` are Wikivoyage articles too; see the README there.
