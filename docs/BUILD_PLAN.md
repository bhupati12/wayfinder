# Wayfinder build plan

Wayfinder is a question-answering API over public travel guides. A user asks a question, the system
finds the most relevant guide passages, and an LLM writes an answer that cites them. This pattern is
called retrieval-augmented generation (RAG).

Build it one phase at a time. Each phase lists what to build, where the code goes, and a
"done when" check. Do not start a phase until the previous one passes its check.

**Stack:** .NET 10, ASP.NET Core, Entity Framework Core, PostgreSQL with pgvector, Redis, xUnit,
Testcontainers, Docker, GitHub Actions, Helm.

**Package names below are suggestions.** Confirm the current package and version in its
documentation when you start each phase, and pin the version you install.

---

## How the repository is organised

```
wayfinder/
  docker-compose.yml          local PostgreSQL (pgvector) and Redis
  docs/                       this plan, evaluation results, architecture notes
  eval/                       evaluation question sets (JSON)
  helm/<service>/             stage-values.yaml and prod-values.yaml per service
  .github/workflows/          one workflow file per service
  src/dotnet/
    Wayfinder.sln
    Directory.Build.props     shared compiler settings (nullable, warnings as errors)
    domains-shared/guides/DomainModels.Guides     DTOs shared between services
    guides/db-entities-guides/GuidesDbEntities    EF Core context, entities, migrations
    guides/importer-guides/GuidesImporter         batch jobs: ingest, embed, evaluate
    guides/service-search/SearchService           HTTP API: /api/search and /api/ask
    connector-llm/Llm.Core                        interfaces and models for LLM calls
    connector-llm/Llm.Infrastructure              provider implementations (HTTP clients)
```

Conventions to keep in every phase:

- **Layering:** Controllers call Services, Services call Database services. No business logic in
  controllers, no EF queries in controllers.
- **Service folder layout:** `Application/Services` with `Interfaces/` beside the implementations,
  `Application/Models`, `Application/Options`, `Application/Validation`, `Database/Services`,
  `Controllers`.
- **Dependency injection:** register everything in `Startup.ConfigureServices`. Bind settings with
  the Options pattern. No secrets in code or in `appsettings.json`.
- **Async:** every I/O method is async and takes a `CancellationToken`.
- **Batch jobs:** a job implements `ICronJob` and is run by name:
  `dotnet GuidesImporter.dll ingestGuides`. The same binary hosts an HTTP API when started with no
  argument. In production each job is a Kubernetes CronJob that calls the binary with its job name.
- **Tests:** each service has a `.Tests` project with `UnitTests/` and `IntegrationTests/`.

---

## Phase 0: Foundations (already done)

The scaffold in this repository covers this phase.

- [x] Solution with nine projects and their references
- [x] Folder layout inside each project
- [x] `Program.cs` and `Startup.cs` for both services, with `/healthz`
- [x] `ICronJob` and `JobRunner` for running jobs by name
- [x] `docker-compose.yml` with PostgreSQL (pgvector) and Redis

What is left for you:

- [ ] Run `git init` and make the first commit
- [ ] Run `docker compose up -d` and confirm both containers are healthy
- [ ] Run `dotnet build src/dotnet/Wayfinder.sln`
- [ ] Run SearchService and open `/healthz`

**Done when:** the solution builds with zero warnings and `/healthz` returns `Healthy`.

---

## Phase 1: Data model and ingestion

**Goal:** guide articles are in PostgreSQL, split into chunks, and re-running the job changes nothing.

**Data source:** the English Wikivoyage dump
(`https://dumps.wikimedia.org/enwikivoyage/latest/enwikivoyage-latest-pages-articles.xml.bz2`).
The content is licensed CC BY-SA, so the README and every answer must credit Wikivoyage and link the
source article. Start with 2,000 to 5,000 articles (one region is enough).

**Where:** `GuidesDbEntities`, `GuidesImporter`.

- [ ] Add EF Core and the Npgsql provider to `GuidesDbEntities`
- [ ] Entities in `Models/`:
  - `GuideArticle`: id, title, URL, raw text hash, last ingested time
  - `GuideChunk`: id, article id, section heading, position, text, token count, content hash
  - `IngestionRun`: id, started, finished, counts of articles added, updated, skipped, failed
- [ ] `DAL/GuidesContext.cs`, entity configurations in `Configurations/`, first migration
- [ ] Options class for ingestion settings (dump path, article limit, chunk size, overlap)
- [ ] `IDumpReader`: stream the XML dump without loading it all into memory
- [ ] `IWikitextCleaner`: strip templates, links, tables and markup down to plain text
- [ ] `IChunker`: split by section heading, then into chunks of about 300 to 500 tokens with a small
      overlap; never split in the middle of a sentence
- [ ] `IngestGuidesJob` (`ICronJob`, name `ingestGuides`): read, clean, chunk, upsert
- [ ] Idempotency: compare the content hash; skip unchanged articles, replace chunks of changed ones
      inside a transaction
- [ ] Write in batches and log progress every N articles
- [ ] Unit tests for the cleaner and the chunker (table-driven, with real article samples)

**Done when:**

- `dotnet run --project ... -- ingestGuides` loads the articles
- a second run reports zero added and zero updated
- the `IngestionRun` row shows the counts

**Learn first:** tokens versus characters, why chunk size and overlap matter for retrieval.

---

## Phase 2: Embeddings

**Goal:** every chunk has a vector stored next to it, and the vector column is indexed.

**Where:** `Llm.Core`, `Llm.Infrastructure`, `GuidesDbEntities`, `GuidesImporter`.

- [ ] `Llm.Core/Interfaces/IEmbeddingClient`: `EmbedAsync(IReadOnlyList<string>, CancellationToken)`
- [ ] `Llm.Infrastructure`: one implementation that calls an embedding API through
      `IHttpClientFactory` (or through `Microsoft.Extensions.AI`'s `IEmbeddingGenerator`)
- [ ] Options for model name, dimensions, batch size, API key (key comes from user secrets or an
      environment variable)
- [ ] Resilience on the HTTP client: per-attempt timeout, retries with exponential backoff on 429
      and 5xx, circuit breaker (`Microsoft.Extensions.Http.Resilience` or Polly)
- [ ] Enable the `vector` extension in a migration; add an `Embedding` column to `GuideChunk`
      (`Pgvector.EntityFrameworkCore`) plus `EmbeddingModel` and `EmbeddedAt`
- [ ] HNSW index on the vector column with cosine distance
- [ ] `EmbedChunksJob` (name `embedChunks`): select chunks with no embedding or a stale content
      hash, embed in batches, save; safe to stop and resume
- [ ] Record tokens used per run so you know the cost
- [ ] Tests: fake `IEmbeddingClient` for the job; one test for retry behaviour against a stub handler

**Done when:** all chunks have embeddings, a re-run embeds zero chunks, and you can state what the
full embedding run cost.

**Learn first:** what an embedding is, cosine similarity, what an HNSW index trades off (speed
versus recall).

---

## Phase 3: Search API

**Goal:** `/api/search` returns the most relevant chunks for a query, using vector search, keyword
search, and a combination of both.

**Where:** `SearchService`.

- [ ] `Database/Services/IChunkSearchRepository` with three methods: vector, keyword, hybrid
- [ ] Vector search: embed the query, order by cosine distance, take top K
- [ ] Keyword search: generated `tsvector` column with a GIN index; rank with `ts_rank`
- [ ] Hybrid search: run both, merge the two ranked lists with reciprocal rank fusion
- [ ] `Application/Services/ISearchService` that picks the mode and maps to response DTOs
- [ ] `Controllers/SearchController`: `GET /api/search?q=&mode=&k=`
- [ ] Request validation (query length limits, K range) with problem-details error responses
- [ ] Response includes chunk text, article title, section, source URL, score
- [ ] Integration tests with Testcontainers PostgreSQL and a small fixed dataset

**Done when:** all three modes return sensible results for ten hand-picked queries, and the
integration tests pass.

**Learn first:** full-text search in PostgreSQL, reciprocal rank fusion.

---

## Phase 4: Answer API

**Goal:** `/api/ask` returns an answer grounded in retrieved chunks, with citations.

**Where:** `Llm.Core`, `Llm.Infrastructure`, `SearchService`.

- [ ] `Llm.Core/Interfaces/IChatClient` wrapper (or use `Microsoft.Extensions.AI`'s `IChatClient`)
      with a streaming method
- [ ] Prompt builder: system instructions, numbered sources, the question; keep the template in one
      place and version it
- [ ] Rules in the prompt: answer only from the sources, cite source numbers, say you do not know
      when the sources do not cover the question
- [ ] `IAnswerService`: retrieve (hybrid), trim sources to a token budget, call the model, map
      citation numbers back to article URLs
- [ ] `Controllers/AskController`: `POST /api/ask` (JSON) and a streaming variant using server-sent
      events
- [ ] Treat retrieved text as data: it must never be able to change the instructions (prompt
      injection). Add a test with a chunk that contains "ignore previous instructions"
- [ ] Return token counts and timings in the response metadata
- [ ] Unit tests for the prompt builder and citation mapping with a fake chat client

**Done when:** answers cite real sources, an off-topic question gets "I don't know", and the
streaming endpoint delivers tokens as they arrive.

**Learn first:** prompt structure for grounded answers, context-window budgeting.

---

## Phase 5: Evaluation

**Goal:** you have numbers that show how good retrieval and answers are. These numbers go on your
resume, so they must be measured, not estimated.

**Where:** `eval/`, `GuidesImporter`, `docs/EVALUATION.md`.

- [ ] `eval/questions.json`: 50 to 100 questions, each with the article (and ideally section) that
      contains the answer. Write them yourself; mix easy, hard, and unanswerable questions
- [ ] `RunEvalJob` (name `runEval`): for each question, run vector, keyword and hybrid search
- [ ] Metrics: recall at 5 and at 10 (was the right chunk in the top K), mean reciprocal rank
- [ ] Answer check: ask a model to judge whether each answer is supported by its sources; verify 20
      to 30 judgements by hand and report the agreement
- [ ] Check that unanswerable questions get refused
- [ ] Write the results table to `docs/EVALUATION.md`, with the date and the settings used
- [ ] Try one improvement (chunk size, overlap, K, or a reranker) and record before and after

**Done when:** `docs/EVALUATION.md` has a results table you can reproduce by re-running the job.

**Learn first:** recall at K, mean reciprocal rank, why LLM judges need a human spot check.

---

## Phase 6: Production readiness

**Goal:** the services behave well under failure, load, and bad input, and can be deployed by a
pipeline. Work through the groups in order.

### 6.1 Configuration and secrets

- [ ] Every setting bound through Options classes with validation at startup (`ValidateOnStart`)
- [ ] `appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json`
- [ ] Secrets only from environment variables or a secret store; add a secret scanner
      (gitleaks) as a pre-commit hook and in CI

### 6.2 API hardening

- [ ] Global exception handling middleware that returns problem details and never leaks stack traces
- [ ] Input limits: query length, request body size, K range
- [ ] API key authentication on `/api/ask`
- [ ] Rate limiting per API key (built-in ASP.NET Core rate limiter, or Redis for multi-instance)
- [ ] Request timeout and cancellation wired through to the database and the LLM call
- [ ] OpenAPI document and a versioned route prefix

### 6.3 Resilience

- [ ] Timeouts, retries with backoff, and circuit breakers on every outbound HTTP client
- [ ] Graceful degradation: if the LLM is unavailable, `/api/search` still works and `/api/ask`
      returns a clear 503
- [ ] Database connection pool sized deliberately; command timeouts set
- [ ] Jobs are resumable and idempotent (re-check Phases 1 and 2)

### 6.4 Performance and cost

- [ ] Redis cache for query embeddings and for full answers (key on normalised question plus
      settings; set a TTL)
- [ ] `AsNoTracking()` and column projections on read paths
- [ ] Record tokens in, tokens out, and estimated cost per request
- [ ] A daily cost cap that switches `/api/ask` off when exceeded

### 6.5 Observability

- [ ] Structured logging with a correlation id per request
- [ ] OpenTelemetry traces and metrics: time in retrieval, time in the LLM, cache hit rate, tokens
- [ ] Health checks split into liveness and readiness (readiness checks PostgreSQL and Redis)
- [ ] Error tracking (for example Sentry) for unhandled exceptions

### 6.6 Testing

- [ ] Unit tests for services, validators, chunker, prompt builder
- [ ] Integration tests with Testcontainers for the repositories and the API
- [ ] Snapshot tests for the prompt template and the API response shape
- [ ] Code coverage report in CI

### 6.7 Delivery

- [ ] Multi-stage `Dockerfile` per service; run as a non-root user
- [ ] `.github/workflows/service-search.yaml` and `importer-guides.yaml`: restore, build, test,
      build image; triggered only by changes under that service's paths
- [ ] Pre-commit config: secret scan and a C# formatter (CSharpier)
- [ ] `helm/service-search/{stage,prod}-values.yaml`: replicas, resources, probes, environment
- [ ] `helm/importer-guides/{stage,prod}-values.yaml`: one CronJob entry per job (`ingestGuides`,
      `embedChunks`) with schedule, `concurrencyPolicy: Forbid`, `backoffLimit`, deadline, resources
- [ ] `CODEOWNERS` and a pull-request template

### 6.8 Load test

- [ ] Load test `/api/search` and `/api/ask` with k6 or a similar tool
- [ ] Record median and 95th-percentile latency, and the request rate at which errors begin
- [ ] Fix the first bottleneck you find and record before and after

**Done when:** CI is green on every push, both services run from their Docker images, the load-test
numbers are written down in `docs/`, and killing PostgreSQL or the LLM produces clean errors rather
than hangs.

---

## Phase 7: Ship it

- [ ] Deploy to a small cloud instance or a managed container service
- [ ] `README.md`: what it is, architecture diagram, how to run it locally, evaluation table,
      load-test numbers, demo link, Wikivoyage attribution
- [ ] A two-minute demo recording
- [ ] Write the resume bullets from the measured numbers:
  - Built a RAG question-answering API over [N] travel guide articles with ASP.NET Core and
    PostgreSQL pgvector, returning cited answers.
  - Raised top-10 retrieval recall from [X]% to [Y]% by combining vector and keyword search,
    measured on a [N]-question evaluation set.
  - Served answers at [P] ms 95th-percentile latency with Redis caching, at [C] cost per query.

**Done when:** a stranger can open the demo link, ask a question, and get a cited answer.

---

## Optional extensions

- **Reranking:** rerank the top 50 hybrid results with a reranker model; measure the recall change.
- **Agent feature:** let the model call `/api/search` as a tool and decide when to search again.
- **Batch embedding:** use a provider's batch API for the embedding job to cut its cost.
- **Multilingual:** ingest a second language and answer in the question's language.

---

## Suggested pace

| Phase | Rough effort |
|---|---|
| 1. Data model and ingestion | 3 to 4 days |
| 2. Embeddings | 2 days |
| 3. Search API | 3 days |
| 4. Answer API | 3 days |
| 5. Evaluation | 2 to 3 days |
| 6. Production readiness | 5 to 7 days |
| 7. Ship it | 2 days |

That is about four weeks at a couple of focused hours a day. Phases 1 to 5 give you a project you
can talk about; Phase 6 is what makes it stand out.
