# Wayfinder

A question-answering API over public travel guides. Ask a question, get an answer that cites the
guide passages it came from (retrieval-augmented generation).

Status: scaffold only. The build plan is in [docs/BUILD_PLAN.md](docs/BUILD_PLAN.md).

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

Run a batch job by name (jobs are added in Phase 1 onward):

```bash
dotnet run --project src/dotnet/guides/importer-guides/GuidesImporter -- ingestGuides
```

## Layout

| Path | Purpose |
|---|---|
| `src/dotnet/guides/service-search/SearchService` | HTTP API: search and ask |
| `src/dotnet/guides/importer-guides/GuidesImporter` | Batch jobs: ingest, embed, evaluate |
| `src/dotnet/guides/db-entities-guides/GuidesDbEntities` | EF Core context, entities, migrations |
| `src/dotnet/domains-shared/guides/DomainModels.Guides` | DTOs shared between services |
| `src/dotnet/connector-llm` | LLM interfaces (`Llm.Core`) and provider clients (`Llm.Infrastructure`) |
| `helm/` | Deployment values per service |
| `eval/` | Evaluation question sets |
| `docs/` | Build plan, evaluation results |

## Data and attribution

Guide content comes from [Wikivoyage](https://www.wikivoyage.org/), licensed CC BY-SA. Answers link
back to the source articles.
