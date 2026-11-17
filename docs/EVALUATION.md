# Evaluation

Measured with the `runEval` job against the running SearchService. Each run below is appended by
the job; rerun it with the same settings to reproduce a row. Raw per-question results are in
`eval/results/`.

- **Recall@K**: share of answerable questions whose answer appears in the top K search results.
  "Article" counts any chunk of the right article; "section" needs the right section too.
- **MRR@10**: mean reciprocal rank of the first correct result (1 for rank 1, 0.5 for rank 2, 0
  when not in the top 10).
- **Supported**: share of answered questions whose answer an LLM judge found supported by the
  cited sources. LLM judges make mistakes; the agreement with a hand check is reported
  separately once `eval/judgements-to-verify.json` has been filled in.

## Summary

The numbers to quote come from the last run, "final with answers, two passes" (17 November 2026,
64 questions, no errors):

| | Hybrid | Vector | Keyword |
|---|---|---|---|
| Recall@10, article | 96% | 96% | 94% |
| Recall@5, section | 96% | 88% | 69% |
| MRR@10, article | 0.897 | 0.898 | 0.821 |

| Answers | |
|---|---|
| Answerable questions answered | 49 / 52 (94%) |
| Unanswerable questions refused | 12 / 12 (100%) |
| Answers citing at least one source | 49 / 49 |
| Judged supported by their sources | 39 / 49 (80%), by an LLM judge, not yet hand-checked |
| Answer time | median 65 s, p95 82 s, on the laptop's integrated GPU |

What the experiments changed (hybrid, against the baseline run):

| Change | Effect | Kept |
|---|---|---|
| Keyword weight 0.5 in the fusion | MRR 0.875 to 0.896, but section recall@5 94% to 92% | no, weight 1 |
| Keyword search ignores accents | no change on the set; lets "bun cha" match "bún chả" | yes |
| Title boost 0 instead of 0.5 | keyword recall@10 92% to 69%, hybrid MRR 0.875 to 0.772 | no |
| Title boost 0.2 instead of 0.5 | hybrid section recall@5 94% to 88% | no, 0.5 kept |
| Keyword search ranks all-word matches first | recall@10 94% to 96%, MRR 0.875 to 0.897, 4 to 5 times faster keyword search | yes |

Hybrid is the default: it matches vector search on article recall and beats it by 8 points on
finding the right section, and it still works, as keyword search, when the embedding model is
down.

Caveats:

- The questions were written with help from the model that built the system, from the indexed
  articles; they have not yet been reviewed by a person. 52 answerable questions is small: one
  question is about 2 points.
- The judge is the same small model (`qwen2.5:3b`) that wrote the answers. Until someone checks
  the 30 verdicts in `eval/judgements-to-verify.json` by hand and runs
  `dotnet run --project src/dotnet/guides/importer-guides/GuidesImporter -- scoreJudgeAgreement`,
  treat "80% supported" as a rough figure.
- The earlier "final with answers" run lost 12 questions to search timeouts, because embedding
  and chat calls swapped models on the GPU; its numbers are superseded.

## baseline retrieval — 2026-11-17 07:48 UTC

Default search settings: hybrid reciprocal rank fusion (k=60) over 50 vector and 50 keyword candidates, article-title matches boosted in keyword search. Retrieval only; answers are evaluated in a separate run.

Settings: 3,896 articles, 33,192 chunks (chunks 300-500 tokens, overlap 50); embedding model `nomic-embed-text`; 52 answerable and 12 unanswerable questions; top 10 results per search; run took 00:01:19. Raw results: `../eval/results/20261117-0748-baseline-retrieval.json`.

| Mode | Recall@5 article | Recall@10 article | Recall@5 section | Recall@10 section | MRR@10 article | Median search ms |
|---|---|---|---|---|---|---|
| hybrid | 94% | 94% | 94% | 94% | 0.875 | 438 |
| vector | 94% | 96% | 88% | 94% | 0.898 | 12 |
| keyword | 88% | 92% | 65% | 77% | 0.794 | 282 |

Recall@10 (article) by difficulty: easy (30): hybrid 90%, vector 93%, keyword 90%; hard (22): hybrid 100%, vector 100%, keyword 95%.

## keyword weight 0.5 — 2026-11-17 07:52 UTC

Experiment: weighted reciprocal rank fusion, keyword list weighted 0.5 against 1 for vector (SearchService started with Search__KeywordWeight=0.5). Everything else as in the baseline. Vector and keyword rows are unaffected by the setting and serve as a check that the run is comparable.

Settings: 3,896 articles, 33,192 chunks (chunks 300-500 tokens, overlap 50); embedding model `nomic-embed-text`; 52 answerable and 12 unanswerable questions; top 10 results per search; run took 00:01:01. Raw results: `../eval/results/20261117-0752-keyword-weight-0-5.json`.

| Mode | Recall@5 article | Recall@10 article | Recall@5 section | Recall@10 section | MRR@10 article | Median search ms |
|---|---|---|---|---|---|---|
| hybrid | 94% | 94% | 92% | 94% | 0.896 | 346 |
| vector | 94% | 96% | 88% | 94% | 0.898 | 12 |
| keyword | 88% | 92% | 65% | 77% | 0.794 | 273 |

Recall@10 (article) by difficulty: easy (30): hybrid 90%, vector 93%, keyword 90%; hard (22): hybrid 100%, vector 100%, keyword 95%.

## unaccent keyword search — 2026-11-17 08:01 UTC

Experiment 2: keyword search ignores diacritics (text search configuration wayfinder_english = english plus unaccent, migration AddUnaccentSearch). Prompted by q31, where the question says 'bun cha' and the guide says 'bún chả'. Fusion weight back to 1; everything else as in the baseline.

Settings: 3,896 articles, 33,192 chunks (chunks 300-500 tokens, overlap 50); embedding model `nomic-embed-text`; 52 answerable and 12 unanswerable questions; top 10 results per search; run took 00:01:00. Raw results: `../eval/results/20261117-0801-unaccent-keyword-search.json`.

| Mode | Recall@5 article | Recall@10 article | Recall@5 section | Recall@10 section | MRR@10 article | Median search ms |
|---|---|---|---|---|---|---|
| hybrid | 94% | 94% | 94% | 94% | 0.875 | 337 |
| vector | 94% | 96% | 88% | 94% | 0.898 | 12 |
| keyword | 88% | 92% | 65% | 77% | 0.795 | 285 |

Recall@10 (article) by difficulty: easy (30): hybrid 90%, vector 93%, keyword 90%; hard (22): hybrid 100%, vector 100%, keyword 95%.

## no title boost — 2026-11-17 08:03 UTC

Experiment 3: title-match boost in keyword search off (Search__TitleMatchBoost=0, default 0.5). Prompted by q31: for 'bun cha' the word 'cha' matches the title of the Thai town Cha-am, and the 0.5 boost outweighs every text-match score, so all top keyword results were Cha-am. Unaccent search kept.

Settings: 3,896 articles, 33,192 chunks (chunks 300-500 tokens, overlap 50); embedding model `nomic-embed-text`; 52 answerable and 12 unanswerable questions; top 10 results per search; run took 00:00:52. Raw results: `../eval/results/20261117-0803-no-title-boost.json`.

| Mode | Recall@5 article | Recall@10 article | Recall@5 section | Recall@10 section | MRR@10 article | Median search ms |
|---|---|---|---|---|---|---|
| hybrid | 92% | 96% | 79% | 94% | 0.772 | 304 |
| vector | 94% | 96% | 88% | 94% | 0.898 | 11 |
| keyword | 56% | 69% | 33% | 42% | 0.400 | 261 |

Recall@10 (article) by difficulty: easy (30): hybrid 93%, vector 93%, keyword 63%; hard (22): hybrid 100%, vector 100%, keyword 77%.

## title boost 0.2 — 2026-11-17 08:04 UTC

Experiment 3b: title-match boost 0.2 (Search__TitleMatchBoost=0.2) instead of 0.5 or 0. Unaccent search kept.

Settings: 3,896 articles, 33,192 chunks (chunks 300-500 tokens, overlap 50); embedding model `nomic-embed-text`; 52 answerable and 12 unanswerable questions; top 10 results per search; run took 00:00:49. Raw results: `../eval/results/20261117-0804-title-boost-0-2.json`.

| Mode | Recall@5 article | Recall@10 article | Recall@5 section | Recall@10 section | MRR@10 article | Median search ms |
|---|---|---|---|---|---|---|
| hybrid | 92% | 96% | 88% | 96% | 0.872 | 284 |
| vector | 94% | 96% | 88% | 94% | 0.898 | 11 |
| keyword | 88% | 92% | 63% | 77% | 0.795 | 246 |

Recall@10 (article) by difficulty: easy (30): hybrid 93%, vector 93%, keyword 90%; hard (22): hybrid 100%, vector 100%, keyword 95%.

## final with answers — 2026-11-17 08:06 UTC

Final settings: hybrid RRF k=60, keyword weight 1, title boost 0.5, unaccent keyword search; answers from qwen2.5:3b through Ollama with answer-prompt-v2, k=6 sources; judged by the same qwen2.5:3b with judge-prompt-v1 (a small model grading itself, so the hand check matters more than usual).

Settings: 3,896 articles, 33,192 chunks (chunks 300-500 tokens, overlap 50); embedding model `nomic-embed-text`; 52 answerable and 12 unanswerable questions; top 10 results per search; run took 01:34:32. Raw results: `../eval/results/20261117-0806-final-with-answers.json`.

| Mode | Recall@5 article | Recall@10 article | Recall@5 section | Recall@10 section | MRR@10 article | Median search ms |
|---|---|---|---|---|---|---|
| hybrid | 83% | 83% | 83% | 83% | 0.769 | 7998 |
| vector | 83% | 85% | 77% | 83% | 0.783 | 480 |
| keyword | 75% | 79% | 56% | 67% | 0.670 | 2251 |

Recall@10 (article) by difficulty: easy (30): hybrid 83%, vector 87%, keyword 83%; hard (22): hybrid 82%, vector 82%, keyword 73%.

| Answers | Value |
|---|---|
| Answerable questions answered | 41 / 45 (91%) |
| Answerable questions refused ("I don't know") | 4 / 45 |
| Unanswerable questions correctly refused | 7 / 7 (100%) |
| Answers with at least one citation | 41 / 41 |
| Judged supported by their sources (LLM judge `qwen2.5:3b`) | 31 / 41 (76%) |
| Judge verdict unclear | 0 |
| Median answer time | 74,968 ms |
| Mean tokens per answer (in / out) | 2539 / 34 |
| Judge agreement with hand check | pending: fill in `eval/judgements-to-verify.json`, then run `scoreJudgeAgreement` |

**12 questions hit an error and count as misses above; see the raw results.** Rerun before quoting these numbers.

## keyword all-words first — 2026-11-17 09:57 UTC

Experiment: keyword search ranks only chunks containing every query word when at least 10 do (Search:KeywordAllWordsMinResults=10), else any-word matches as before. Load-test fix; this run checks relevance.

Settings: 3,896 articles, 33,192 chunks (chunks 300-500 tokens, overlap 50); embedding model `nomic-embed-text`; 52 answerable and 12 unanswerable questions; top 10 results per search; run took 00:01:08. Raw results: `../eval/results/20261117-0957-keyword-all-words-first.json`.

| Mode | Recall@5 article | Recall@10 article | Recall@5 section | Recall@10 section | MRR@10 article | Median search ms |
|---|---|---|---|---|---|---|
| hybrid | 96% | 96% | 96% | 96% | 0.897 | 370 |
| vector | 94% | 96% | 88% | 94% | 0.898 | 13 |
| keyword | 92% | 94% | 69% | 79% | 0.821 | 282 |

Recall@10 (article) by difficulty: easy (30): hybrid 93%, vector 93%, keyword 90%; hard (22): hybrid 100%, vector 100%, keyword 100%.

## final with answers, two passes — 2026-11-17 10:36 UTC

Final settings: hybrid RRF k=60, keyword weight 1, title boost 0.5, unaccent keyword search, all-words-first keyword ranking (KeywordAllWordsMinResults=10); answers from qwen2.5:3b through Ollama with answer-prompt-v2, k=6 sources; judged by the same qwen2.5:3b with judge-prompt-v1 (a small model grading itself, so the hand check matters more than usual). Retrieval runs for every question before any answer, so the laptop's one GPU slot is not swapped between the embedding and chat models; the earlier run interleaved them and lost 12 questions to search timeouts.

Settings: 3,896 articles, 33,192 chunks (chunks 300-500 tokens, overlap 50); embedding model `nomic-embed-text`; 52 answerable and 12 unanswerable questions; top 10 results per search; run took 01:30:13. Raw results: `../eval/results/20261117-1036-final-with-answers-two-passes.json`.

| Mode | Recall@5 article | Recall@10 article | Recall@5 section | Recall@10 section | MRR@10 article | Median search ms |
|---|---|---|---|---|---|---|
| hybrid | 96% | 96% | 96% | 96% | 0.897 | 369 |
| vector | 94% | 96% | 88% | 94% | 0.898 | 12 |
| keyword | 92% | 94% | 69% | 79% | 0.821 | 235 |

Recall@10 (article) by difficulty: easy (30): hybrid 93%, vector 93%, keyword 90%; hard (22): hybrid 100%, vector 100%, keyword 100%.

| Answers | Value |
|---|---|
| Answerable questions answered | 49 / 52 (94%) |
| Answerable questions refused ("I don't know") | 3 / 52 |
| Unanswerable questions correctly refused | 12 / 12 (100%) |
| Answers with at least one citation | 49 / 49 |
| Judged supported by their sources (LLM judge `qwen2.5:3b`) | 39 / 49 (80%) |
| Judge verdict unclear | 0 |
| Median answer time | 65,071 ms |
| Mean tokens per answer (in / out) | 2769 / 34 |
| Judge agreement with hand check | pending: fill in `eval/judgements-to-verify.json`, then run `scoreJudgeAgreement` |

