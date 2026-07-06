# RBL Phase 1 Addendum

This addendum supersedes section `3.3 Smoke Test` and section `3.4 Benchmark Baseline (before/after)` in [RBL_PHASE1_PLAN.md](/D:/projectCode/SWP/SU26SWP10-AI-Study-Hub-team-4/docs/RBL_PHASE1_PLAN.md).

## 3.3 Smoke Test

Objective: verify the flow `upload -> ingest -> vector search -> grounded answer` works end-to-end with `OllamaEmbeddingService`.

1. Start Ollama:
   - `docker compose -f infra/ollama/docker-compose.yml up -d`
2. Wait until Ollama is healthy and model `all-minilm:l6-v2` is ready.
3. Start the app at `http://localhost:5240/`.
4. Sign in with a student test account.
5. Upload 1 PDF whose content is easy to verify manually.
6. Wait until the uploaded document reaches status `Ready`.
7. Verify ingestion in the database:
   - `document_chunks` contains rows for the uploaded `document_id`
   - `embedding` is not `NULL`
   - `embedding_model = 'all-minilm:l6-v2'`
8. Open chat and ask 1 question grounded in the uploaded PDF.
9. Verify the answer quality:
   - answer includes citations such as `[S1]`, `[S2]`
   - cited source points to the correct file and page/chunk
   - answer is relevant to the PDF, not generic world knowledge
10. Run admin re-ingest:
   - call `POST /api/admin/documents/reingest-all`
   - verify response returns `Succeeded > 0`
   - verify the environment does not report re-ingest failures
11. Ask the same question again after re-ingest.
12. Verify the answer is still grounded and retrieval does not mix chunks from an old embedding model.

### Pass Criteria

- Upload succeeds and the document becomes `Ready`
- Real chunk embeddings are stored in the database
- Chat returns valid citations
- Re-ingest succeeds without breaking search/chat

### Fail Criteria

- Ollama is unreachable
- Document remains `Failed`
- `document_chunks.embedding_model` is empty or wrong
- Chat answer has no citation or is unrelated to the uploaded document

## 3.4 Benchmark Baseline (Before / After)

Objective: provide quantitative evidence for replacing fake embeddings with real embeddings.

### Fixed Benchmark Setup

- Use the same `3 Vietnamese PDFs`
- Use the same `10 Vietnamese benchmark questions`
- Each question must have an expected relevant file/page/chunk prepared in advance
- Keep `TopK = 5`
- Run on the same dev machine whenever possible

### Before / After Definition

- `Before`: the last branch or commit still using `FakeEmbeddingService`
- `After`: the current branch using `OllamaEmbeddingService`
- If the `before` revision is no longer available, rename this section to `Current Baseline` instead of claiming a strict before/after comparison

### Metrics Table

| Metric | Before | After | How to measure | Notes |
|---|---|---|---|---|
| Ingest duration / document |  |  | Measure from upload completion until document becomes `Ready` | Run at least 3 times and average |
| Chunk success rate |  |  | `successful_chunks / total_chunks` from logs or DB | Should be > 0 and not fail completely |
| Search recall@5 |  |  | For 10 questions, check whether the expected chunk appears in top 5 | `recall@5 = hits / 10` |
| Citation accuracy |  |  | Use benchmark runner or manually verify answer-source alignment | Can use `BenchmarkResult.CitationAccuracy` |
| Hallucination rate |  |  | Use benchmark runner | Can use `BenchmarkResult.HallucinationRate` |
| Refusal accuracy |  |  | Use benchmark runner for missing-information questions | Can use `BenchmarkResult.RefusalAccuracy` |
| Chat latency p50 |  |  | Run `POST /api/benchmark/run` and record `P50LatencyMs` | End-to-end latency |
| Chat latency p95 |  |  | Run `POST /api/benchmark/run` and record `P95LatencyMs` | Same dataset for both runs |
| Overall benchmark score |  |  | Record `OverallScore` from benchmark runner | Only compare when dataset is unchanged |
| Ollama RAM peak | N/A |  | Observe `docker stats aistudy-ollama` during ingest | Relevant for `after` only |

### End-to-End Benchmark Procedure

1. Upload the 3 benchmark PDFs.
2. Record their `document_ids`.
3. Call `POST /api/benchmark/run`.
4. Pass the benchmark `DocumentIds` and `Count = 10`.
5. Save these fields from the response:
   - `CitationAccuracy`
   - `HallucinationRate`
   - `RefusalAccuracy`
   - `TutoringQuality`
   - `DiagramAccuracy`
   - `OverallScore`
   - `P50LatencyMs`
   - `P95LatencyMs`

### Manual Retrieval Baseline

1. Prepare a table mapping `question -> expected file/page/chunk`.
2. Run each query with `TopK = 5`.
3. Mark `Hit@5 = 1` if the expected chunk appears in top 5, otherwise `0`.
4. Compute `Recall@5 = total hits / total questions`.

### Evidence to Keep With the Report

- Screenshot or log from `docker stats`
- JSON response from `POST /api/benchmark/run`
- Table of `10 questions + expected chunks + hit/miss`
- Commit hash or branch name for both `before` and `after`
