# Load test

Measured on 17 November 2026 with [k6](https://k6.io/) 2.4.0 against SearchService running locally
in Release mode, on the same laptop as PostgreSQL, Redis, Ollama and k6 itself (12 logical cores,
integrated GPU, 7 GB RAM). The numbers show where this setup saturates and what the first
bottleneck was; a server with its own database host would be faster. Scripts are in `loadtest/`.

For these runs the per-IP search limit (300 a minute) and the per-key ask limit (10 a minute) were
raised with `RateLimiting__SearchPermitsPerMinute` and `RateLimiting__AskPermitsPerMinute`, since
every k6 user shares one IP and one key. The limiter itself is covered by the integration tests.

## Search

`loadtest/search.js` ramps from 1 to 40 virtual users over two minutes (5, 10, 20, 40), each
sending searches back to back with no pause, over 12 travel queries. Query embeddings are cached in
Redis after the first request, so these runs measure the database and the fusion, not the
embedding model.

| Run | Requests | Throughput | Median | p95 | p99 | Errors |
|---|---|---|---|---|---|---|
| keyword, before | 2,797 | 20.7 req/s | 478 ms | 2,060 ms | 2,820 ms | 0 |
| hybrid, before | 2,415 | 17.9 req/s | 581 ms | 2,150 ms | 3,220 ms | 0 |
| keyword, after | 13,156 | 97.4 req/s | 44 ms | 657 ms | 1,010 ms | 0 |
| hybrid, after | 10,639 | 78.8 req/s | 74 ms | 737 ms | 1,060 ms | 0 |
| vector, after | 62,291 | 461 req/s | 25 ms | 62 ms | 76 ms | 0 |

The script's thresholds (p95 under 1 s, under 1% errors) failed before the fix and pass after it.

### The first bottleneck: ranking every keyword match

Vector search was never the problem: the HNSW index returns 50 neighbours in a few milliseconds.
Keyword search was. It OR-ed the query words, so every chunk containing any of them had to be
scored by `ts_rank_cd`, and common words match a large share of the 33,192 chunks:

| Query | Chunks matching any word | Chunks matching every word |
|---|---|---|
| how to get from the airport to Seoul | 14,139 | 225 |
| street food in Hanoi | 10,440 | 13 |
| night market in Taipei | 7,331 | 39 |
| is Bangkok safe at night | 6,181 | 16 |

`EXPLAIN (ANALYZE, BUFFERS)` showed that the index lookup took 3 ms and ranking took the rest
(about 145 of 179 ms for the first query): each match's tsvector has to be read, mostly out of
TOAST storage (172 MB, larger than the 128 MB of shared buffers), and scored. Cost grew linearly
with the number of matches, about 30 µs per chunk.

**Fix:** keyword search now first ranks only the chunks that contain every query word, and falls
back to ranking any-word matches when fewer than 10 chunks contain them all
(`Search:KeywordAllWordsMinResults`). Long natural-language questions rarely have 10 all-word
matches, so they still get the any-word ranking; short queries, the common case for a search box,
get the cheap one. Both passes score with the same formula, so a chunk's score does not depend on
which pass found it.

Single-request times, warm cache:

| Query | Before | After |
|---|---|---|
| how to get from the airport to Seoul | 459 ms | 120 ms |
| street food in Hanoi | 270 ms | 14 ms |
| visa for Singapore | 36 ms | 14 ms |

The change also improved relevance on the 52-question evaluation set (hybrid recall@10 94% to 96%,
hybrid MRR 0.875 to 0.897, keyword MRR 0.794 to 0.821; see
[EVALUATION.md](EVALUATION.md), "keyword all-words first"): requiring every word removes chunks
that only matched a filler word such as "get".

Alternatives considered and not needed yet: storing tsvectors uncompressed (`ALTER COLUMN ...
SET STORAGE EXTERNAL`) cut ranking time by about 30% in a test, and a larger `shared_buffers`
would keep TOAST pages in memory; a search-result cache would hide the cost for repeated queries
only.

### Where it saturates

A stress run of hybrid search ramped to 80, 160 and then 320 virtual users over 90 seconds:

| Users (end of step) | Throughput | Median | p95 |
|---|---|---|---|
| up to 80 | 113 to 133 req/s | 48 to 420 ms | 412 to 1,139 ms |
| up to 160 | 105 to 132 req/s | 598 to 1,203 ms | 1,357 to 2,227 ms |
| up to 320 | 109 to 134 req/s | 1,268 to 2,057 ms | 2,181 to 3,248 ms |

(Each row covers three or four 10-second windows of the run; the ranges are across those windows.)

Throughput levels off at about **125 hybrid searches a second**; beyond that, extra users only
queue, and latency grows with them. **No request failed, up to 320 concurrent users** (12,365
requests). Errors would start when queueing passes the search request timeout (15 s in
Development, 10 s in Production), at well over 320 users. k6 shared the 12 cores with the API and
PostgreSQL, so part of that ceiling is the load generator itself.

## Answers

`loadtest/ask.js` ran 2 virtual users for 5 minutes, cycling through 4 questions, with the answer
cache emptied first.

| | Value |
|---|---|
| Requests | 3,277, none failed |
| Freshly generated answers | 7, taking 54 to 124 s each |
| Cached answers | 3,270; median 7 ms, p95 23 ms |

The local model generates one answer at a time, so a second concurrent question waits for the
first: the 124 s answer is one that queued behind another. On this hardware `/api/v1/ask`
handles about one new question a minute, and a hosted model is the way to serve more; the cache
makes repeated questions effectively free.

**Known gap:** 7 answers were generated for 4 questions. When two users ask the same new question
at the same moment, both miss the cache and both generate. A per-key lock (single flight) around
generation would remove the duplicate work; it is not needed at this scale.

## Failure behaviour

Checked against the Docker image of SearchService (Production settings) by stopping each
dependency while the API was running:

| Dependency stopped | Request | Result |
|---|---|---|
| PostgreSQL | any search | 503 problem details, `Retry-After: 10`, in 30 to 180 ms |
| PostgreSQL | `/ask`, new question | 503 in 270 ms |
| PostgreSQL | `/ask`, cached question | 200 from the cache |
| PostgreSQL | `/healthz/ready` | 503; `/healthz/live` stays 200 |
| PostgreSQL | back up | next request succeeds, no restart |
| Ollama | hybrid search | 200, keyword results with `degraded: true` |
| Ollama | vector search | 503, `Retry-After: 30`, in 200 ms |
| Ollama | keyword search | unaffected |
| Ollama | `/ask`, new question | 503, `Retry-After: 30`, in 2.5 s (retries, then the circuit breaker) |
| Ollama | `/ask`, cached question | 200 from the cache |

Nothing hung. The first PostgreSQL test returned a generic 500 instead of a 503; that is now fixed
by a handler that maps transient Npgsql failures to 503, including `57P01` ("terminating
connection due to administrator command"), which the first request after a shutdown receives.
