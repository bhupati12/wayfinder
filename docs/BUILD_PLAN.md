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

- [x] Run `git init` and make the first commit
- [x] Run `docker compose up -d` and confirm both containers are healthy
- [x] Run `dotnet build src/dotnet/Wayfinder.sln`
- [x] Run SearchService and open `/healthz`

**Done when:** the solution builds with zero warnings and `/healthz` returns `Healthy`.

---

## Phase 1: Data model and ingestion

**Goal:** guide articles are in PostgreSQL, split into chunks, and re-running the job changes nothing.

**Data source:** the English Wikivoyage dump
(`https://dumps.wikimedia.org/enwikivoyage/latest/enwikivoyage-latest-pages-articles.xml.bz2`).
The content is licensed CC BY-SA, so the README and every answer must credit Wikivoyage and link the
source article. Start with 2,000 to 5,000 articles (one region is enough).

**Where:** `GuidesDbEntities`, `GuidesImporter`.

- [x] Add EF Core and the Npgsql provider to `GuidesDbEntities`
- [x] Entities in `Models/`:
  - `GuideArticle`: id, title, URL, raw text hash, last ingested time
  - `GuideChunk`: id, article id, section heading, position, text, token count, content hash
  - `IngestionRun`: id, started, finished, counts of articles added, updated, skipped, failed
- [x] `DAL/GuidesContext.cs`, entity configurations in `Configurations/`, first migration
- [x] Options class for ingestion settings (dump path, article limit, chunk size, overlap)
- [x] `IDumpReader`: stream the XML dump without loading it all into memory
- [x] `IWikitextCleaner`: strip templates, links, tables and markup down to plain text
- [x] `IChunker`: split by section heading, then into chunks of about 300 to 500 tokens with a small
      overlap; never split in the middle of a sentence
- [x] `IngestGuidesJob` (`ICronJob`, name `ingestGuides`): read, clean, chunk, upsert
- [x] Idempotency: compare the content hash; skip unchanged articles, replace chunks of changed ones
      inside a transaction
- [x] Write in batches and log progress every N articles
- [x] Unit tests for the cleaner and the chunker (table-driven, with real article samples)

**Done when:**

- `dotnet run --project ... -- ingestGuides` loads the articles
- a second run reports zero added and zero updated
- the `IngestionRun` row shows the counts

**Learn first:** tokens versus characters, why chunk size and overlap matter for retrieval.

**Result (17 November 2026, dump of 1 November 2026):** regions East Asia and Southeast Asia,
3,896 articles, 33,192 chunks, 10.9 million tokens (cl100k_base). Chunk median 342 tokens, maximum
499; 0.5% are under 100 tokens. First run 3 min 11 s (3,896 added); second run 0 added, 0 updated,
3,896 skipped. Settings: min 300, max 500, overlap 50 tokens.

Decisions worth knowing:

- Articles are keyed on the MediaWiki page id, so a renamed article is updated, not duplicated.
- Besides the raw text hash, each article stores a processing signature (cleaner version plus
  chunk settings). Changing chunk size or overlap rebuilds every article on the next run, which is
  what the Phase 5 chunk-size experiment needs.
- Listing templates (`{{see}}`, `{{eat}}`, `{{sleep}}`, ...) are rendered as one line with name,
  description, address, hours and price instead of being stripped, because they hold most of the
  practical content. Other templates are dropped.
- Short sections merge with the next one, heading included; a full chunk breaks at the start of a
  line where possible, so a listing stays whole. Overlap is whole lines at a line break and
  trailing sentences mid-paragraph, never across a heading.
- `migrateDatabase` is a job too, so a deploy can run migrations the same way it runs ingestion.

---

## Phase 2: Embeddings

**Goal:** every chunk has a vector stored next to it, and the vector column is indexed.

**Where:** `Llm.Core`, `Llm.Infrastructure`, `GuidesDbEntities`, `GuidesImporter`.

- [x] `Llm.Core/Interfaces/IEmbeddingClient`: `EmbedAsync(IReadOnlyList<string>, CancellationToken)`
- [x] `Llm.Infrastructure`: one implementation that calls an embedding API through
      `IHttpClientFactory` (or through `Microsoft.Extensions.AI`'s `IEmbeddingGenerator`)
- [x] Options for model name, dimensions, batch size, API key (key comes from user secrets or an
      environment variable)
- [x] Resilience on the HTTP client: per-attempt timeout, retries with exponential backoff on 429
      and 5xx, circuit breaker (`Microsoft.Extensions.Http.Resilience` or Polly)
- [x] Enable the `vector` extension in a migration; add an `Embedding` column to `GuideChunk`
      (`Pgvector.EntityFrameworkCore`) plus `EmbeddingModel` and `EmbeddedAt`
- [x] HNSW index on the vector column with cosine distance
- [x] `EmbedChunksJob` (name `embedChunks`): select chunks with no embedding or a stale content
      hash, embed in batches, save; safe to stop and resume
- [x] Record tokens used per run so you know the cost
- [x] Tests: fake `IEmbeddingClient` for the job; one test for retry behaviour against a stub handler

**Done when:** all chunks have embeddings, a re-run embeds zero chunks, and you can state what the
full embedding run cost.

**Learn first:** what an embedding is, cosine similarity, what an HNSW index trades off (speed
versus recall).

**Result (17 November 2026):** all 33,192 chunks embedded with `nomic-embed-text` (768
dimensions) on a local Ollama, so the run cost $0. The full run embedded 32,680 chunks (the other
512 came from four batch-size trials) in 2 h 49 min: 511 requests, 10.76 million input tokens,
3.2 chunks/s, 0 failures. A re-run found 0 chunks to embed and finished in 0.07 s. Throughput
was 1.2 chunks/s on the CPU alone; the integrated GPU (`OLLAMA_IGPU_ENABLE=1`) and batches of 64
brought it to 3.2. `guide_chunks` with vectors, HNSW and GIN indexes is 383 MB. A paid API at
$0.02 per million tokens would have cost about $0.22.

---

## Phase 3: Search API

**Goal:** `/api/search` returns the most relevant chunks for a query, using vector search, keyword
search, and a combination of both.

**Where:** `SearchService`.

- [x] `Database/Services/IChunkSearchRepository` with three methods: vector, keyword, hybrid
- [x] Vector search: embed the query, order by cosine distance, take top K
- [x] Keyword search: generated `tsvector` column with a GIN index; rank with `ts_rank`
- [x] Hybrid search: run both, merge the two ranked lists with reciprocal rank fusion
- [x] `Application/Services/ISearchService` that picks the mode and maps to response DTOs
- [x] `Controllers/SearchController`: `GET /api/search?q=&mode=&k=`
- [x] Request validation (query length limits, K range) with problem-details error responses
- [x] Response includes chunk text, article title, section, source URL, score
- [x] Integration tests with Testcontainers PostgreSQL and a small fixed dataset

**Done when:** all three modes return sensible results for ten hand-picked queries, and the
integration tests pass.

**Learn first:** full-text search in PostgreSQL, reciprocal rank fusion.

**Result (17 November 2026):** routes ended up under `/api/v1` (Phase 6). Ten hand-picked queries,
top three results per mode:

| Query | Vector | Keyword | Hybrid |
|---|---|---|---|
| temples in Kyoto | Kansai / See > Temples | Kyoto / Connect | Kyoto (prefecture) / Get in, Kyoto / See |
| street food in Hanoi | Hanoi / Eat > Budget | Hanoi / See > Temples | Hanoi / Eat > Budget |
| how to get from the airport to Seoul | Seoul / Get in | Seoul / Get in | Seoul / Get in, Gimpo, Incheon ground transport |
| Angkor Wat tickets | Angkor / Get in, Fees and permits (3rd) | Angkor / See > Angkor Wat | Angkor / See, Get in |
| night market in Taipei | Taipei / Eat > Night markets | Taipei / Eat > Night markets | Taipei / Eat > Night markets |
| is Bangkok safe at night | Bangkok / Sleep, Stay safe (3rd) | Bangkok / Districts | Bangkok / Sleep, Stay safe |
| ferry to Boracay | Boracay / Get in > By road and ferry | Boracay / Sleep > Mid-range | Boracay / Get in > By boat |
| visa on arrival Indonesia | Indonesia / Get in > Visa on arrival | same | same |
| Great Wall from Beijing | Beijing / See | Great Wall / Understand > History | Beijing / See |
| hiking Mount Fuji | Mount Fuji / Do | Mount Heng (Hunan) | Mount Fuji / Introduction |

Vector search finds the intent; keyword search goes wrong when common words dominate ("safe",
"night", "ferry"); hybrid kept the vector result in the top two every time and repaired keyword's
misses. Phase 5 measures this properly; several of these queries are also in `loadtest/search.js`.

---

## Phase 4: Answer API

**Goal:** `/api/ask` returns an answer grounded in retrieved chunks, with citations.

**Where:** `Llm.Core`, `Llm.Infrastructure`, `SearchService`.

- [x] `Llm.Core/Interfaces/IChatClient` wrapper (or use `Microsoft.Extensions.AI`'s `IChatClient`)
      with a streaming method
- [x] Prompt builder: system instructions, numbered sources, the question; keep the template in one
      place and version it
- [x] Rules in the prompt: answer only from the sources, cite source numbers, say you do not know
      when the sources do not cover the question
- [x] `IAnswerService`: retrieve (hybrid), trim sources to a token budget, call the model, map
      citation numbers back to article URLs
- [x] `Controllers/AskController`: `POST /api/ask` (JSON) and a streaming variant using server-sent
      events
- [x] Treat retrieved text as data: it must never be able to change the instructions (prompt
      injection). Add a test with a chunk that contains "ignore previous instructions"
- [x] Return token counts and timings in the response metadata
- [x] Unit tests for the prompt builder and citation mapping with a fake chat client

**Done when:** answers cite real sources, an off-topic question gets "I don't know", and the
streaming endpoint delivers tokens as they arrive.

**Learn first:** prompt structure for grounded answers, context-window budgeting.

**Result (17 November 2026), local `qwen2.5:3b` on Ollama, k=6:** five answerable questions and
one off-topic question (northern lights in Norway, outside the ingested regions).

| Prompt | Answered | Answers with citations | Off-topic refused |
|---|---|---|---|
| `answer-prompt-v1` (rules in the system message only) | 5 / 5 | 3 / 5 | yes |
| v2 draft (citation and "I don't know" rules repeated after the question) | 3 / 5 | 3 / 3 | yes |
| `answer-prompt-v2` (only the citation rule repeated, with an example) | 5 / 5 | 5 / 5 | yes |

The 3B model forgot the citation rule after about 3,000 tokens of sources, mostly on longer
answers. Repeating it after the question fixed that; repeating the refusal rule there too made it
refuse answerable questions. Correct citations still do not guarantee a correct answer: asked how
to get from Bangkok to Siem Reap, v2 cited a Bangkok canal-boat section. That is what the Phase 5
judge and its hand check are for.

The answer cache key now includes a hash of the prompt text as well as its version, after the
v2 draft and v2 shared a version and the cache served draft answers.

Streaming: for a new question, the `sources` event and the first token arrived at 67.6 s (prompt
processing of about 3,000 tokens on the laptop's integrated GPU), then 116 token events over
17 s, a median of 144 ms apart, then `done`. The stream waits for the model's first token before
sending `sources` on purpose, so an unavailable model is a clean 503 rather than a broken stream;
with a hosted model that wait is well under a second. Non-streaming answers took 55 to 105 s end
to end on this laptop.

---

## Phase 5: Evaluation

**Goal:** you have numbers that show how good retrieval and answers are. These numbers go on your
resume, so they must be measured, not estimated.

**Where:** `eval/`, `GuidesImporter`, `docs/EVALUATION.md`.

- [ ] `eval/questions.json`: 50 to 100 questions, each with the article (and ideally section) that
      contains the answer. Write them yourself; mix easy, hard, and unanswerable questions
- [x] `RunEvalJob` (name `runEval`): for each question, run vector, keyword and hybrid search
- [x] Metrics: recall at 5 and at 10 (was the right chunk in the top K), mean reciprocal rank
- [ ] Answer check: ask a model to judge whether each answer is supported by its sources; verify 20
      to 30 judgements by hand and report the agreement
- [x] Check that unanswerable questions get refused
- [x] Write the results table to `docs/EVALUATION.md`, with the date and the settings used
- [x] Try one improvement (chunk size, overlap, K, or a reranker) and record before and after

**Done when:** `docs/EVALUATION.md` has a results table you can reproduce by re-running the job.

**Learn first:** recall at K, mean reciprocal rank, why LLM judges need a human spot check.

**Result (17 November 2026):** 64 questions (52 answerable, 12 not) were drafted with Claude from
the indexed articles, so the two open boxes above are yours: review the questions, and hand-check
the 30 verdicts in `eval/judgements-to-verify.json`, then run `scoreJudgeAgreement`. Final run:
hybrid recall@10 96% (article), recall@5 96% (section), MRR 0.897; section recall@5 88% for vector
and 69% for keyword alone. 49 of 52 answerable questions answered with citations, 12 of 12
unanswerable refused, 80% judged supported by the same small model (not yet hand-checked).
Improvements tried: fusion weight, accent-insensitive keyword search, title boost, and all-words-
first keyword ranking (the one that helped: hybrid recall@10 94% to 96%). Details and every run in
[EVALUATION.md](EVALUATION.md).

---

## Phase 6: Production readiness

**Goal:** the services behave well under failure, load, and bad input, and can be deployed by a
pipeline. Work through the groups in order.

### 6.1 Configuration and secrets

- [x] Every setting bound through Options classes with validation at startup (`ValidateOnStart`)
- [x] `appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json`
- [x] Secrets only from environment variables or a secret store; add a secret scanner
      (gitleaks) as a pre-commit hook and in CI

### 6.2 API hardening

- [x] Global exception handling middleware that returns problem details and never leaks stack traces
- [x] Input limits: query length, request body size, K range
- [x] API key authentication on `/api/ask`
- [x] Rate limiting per API key (built-in ASP.NET Core rate limiter, or Redis for multi-instance)
- [x] Request timeout and cancellation wired through to the database and the LLM call
- [x] OpenAPI document and a versioned route prefix

### 6.3 Resilience

- [x] Timeouts, retries with backoff, and circuit breakers on every outbound HTTP client
- [x] Graceful degradation: if the LLM is unavailable, `/api/search` still works and `/api/ask`
      returns a clear 503
- [x] Database connection pool sized deliberately; command timeouts set
- [x] Jobs are resumable and idempotent (re-check Phases 1 and 2)

### 6.4 Performance and cost

- [x] Redis cache for query embeddings and for full answers (key on normalised question plus
      settings; set a TTL)
- [x] `AsNoTracking()` and column projections on read paths
- [x] Record tokens in, tokens out, and estimated cost per request
- [x] A daily cost cap that switches `/api/ask` off when exceeded

### 6.5 Observability

- [x] Structured logging with a correlation id per request
- [x] OpenTelemetry traces and metrics: time in retrieval, time in the LLM, cache hit rate, tokens
- [x] Health checks split into liveness and readiness (readiness checks PostgreSQL and Redis)
- [x] Error tracking (for example Sentry) for unhandled exceptions

### 6.6 Testing

- [x] Unit tests for services, validators, chunker, prompt builder
- [x] Integration tests with Testcontainers for the repositories and the API
- [x] Snapshot tests for the prompt template and the API response shape
- [x] Code coverage report in CI

### 6.7 Delivery

- [x] Multi-stage `Dockerfile` per service; run as a non-root user
- [x] `.github/workflows/service-search.yaml` and `importer-guides.yaml`: restore, build, test,
      build image; triggered only by changes under that service's paths
- [x] Pre-commit config: secret scan and a C# formatter (CSharpier)
- [x] `helm/service-search/{stage,prod}-values.yaml`: replicas, resources, probes, environment
- [x] `helm/importer-guides/{stage,prod}-values.yaml`: one CronJob entry per job (`ingestGuides`,
      `embedChunks`) with schedule, `concurrencyPolicy: Forbid`, `backoffLimit`, deadline, resources
- [x] `CODEOWNERS` and a pull-request template

### 6.8 Load test

- [x] Load test `/api/search` and `/api/ask` with k6 or a similar tool
- [x] Record median and 95th-percentile latency, and the request rate at which errors begin
- [x] Fix the first bottleneck you find and record before and after

**Done when:** CI is green on every push, both services run from their Docker images, the load-test
numbers are written down in `docs/`, and killing PostgreSQL or the LLM produces clean errors rather
than hangs.

**Result (17 November 2026):** done on this machine except CI, which cannot go green until the
repository has a GitHub remote (the workflows are written but have never run). Both images build
(about 240 MB each), run as a non-root user and answered correctly against the local database,
Redis and Ollama. Load test: keyword search was the first bottleneck (ranking every chunk that
matched any query word); ranking all-word matches first took hybrid search from 17.9 to 78.8
requests a second and p95 from 2.15 s to 0.74 s at 40 users; it levels off at about 125 requests a
second with no errors up to 320 users. Stopping PostgreSQL gives 503s with `Retry-After` within
0.3 s; stopping Ollama degrades hybrid search to keyword and returns 503 from `/ask`; cached answers
keep working in both cases. Details in [LOADTEST.md](LOADTEST.md). `CODEOWNERS` still has a
placeholder owner.

---

## Phase 7: Ship it

- [ ] Deploy to a small cloud instance or a managed container service
- [ ] `README.md`: what it is, architecture diagram, how to run it locally, evaluation table,
      load-test numbers, demo link, Wikivoyage attribution (all there except the demo link)
- [ ] A two-minute demo recording
- [x] Write the resume bullets from the measured numbers:
  - Built a RAG question-answering API over [N] travel guide articles with ASP.NET Core and
    PostgreSQL pgvector, returning cited answers.
  - Raised top-10 retrieval recall from [X]% to [Y]% by combining vector and keyword search,
    measured on a [N]-question evaluation set.
  - Served answers at [P] ms 95th-percentile latency with Redis caching, at [C] cost per query.

**Done when:** a stranger can open the demo link, ask a question, and get a cited answer.

**Result (17 November 2026):** the resume bullets, filled in from the measured numbers. The
template's "raised top-10 recall from X% to Y%" does not hold at article level (vector search alone
already reaches 96%), so the second bullet uses section-level recall, where fusion does help:

- Built a retrieval-augmented question-answering API over 3,896 Wikivoyage travel guide articles
  (33,192 chunks) with ASP.NET Core, PostgreSQL pgvector and local models, returning answers that
  cite their sources and refusing questions the guides do not cover (12 of 12 in evaluation).
- Raised top-5 section-level retrieval recall from 88% (vector search) to 96% by fusing vector
  and full-text search with reciprocal rank fusion, measured on a 64-question evaluation set.
- Found the first bottleneck with k6 and `EXPLAIN ANALYZE` and fixed it, taking hybrid search from
  18 to 79 requests a second and p95 latency from 2.15 s to 0.74 s; cached answers are served at
  23 ms p95, at $0 per query on local models.

Left for you: deploy it (the Docker images and Helm values are ready; a hosted model would bring
answer latency down from about a minute), add the demo link to the README, and record the demo.

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
