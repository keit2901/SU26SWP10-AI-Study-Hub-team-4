# Comprehensive Test Guide — Phase 1 + 2 + 3

> **Project**: AI Study Hub v2  
> **App**: .NET 8 Blazor Server + MudBlazor  
> **Port**: http://localhost:5240  
> **Date**: 2026-07-02

---

## Table of Contents

- [Prerequisites](#-prerequisites)
- [Phase 1 — Real Embedding (Ollama)](#-phase-1--real-embedding-ollama)
- [Phase 2 — Semantic Chunking](#-phase-2--semantic-chunking)
- [Phase 3 — RAG Quality](#-phase-3--rag-quality)
- [Full Integration Smoke Test](#-full-integration-smoke-test)
- [Configuration Reference](#-configuration-reference)
- [Appendix: Useful Commands](#-appendix-useful-commands)

---

## 🔧 Prerequisites

Run once before testing.

| # | Step | Command | Expected |
|---|------|---------|----------|
| 0a | Check Docker is running | `docker info` | No error |
| 0b | Start Ollama | `docker compose -f infra\ollama\docker-compose.yml up -d` | Container `aistudy-ollama` started |
| 0c | Verify Ollama model | `curl.exe -s http://localhost:11434/api/tags` | `all-minilm:l6-v2` in response |
| 0d | Start Supabase | `docker compose -f infra\supabase\docker-compose.yml up -d` | All containers healthy |
| 0e | Apply all migrations | `dotnet ef database update --project AI_Study_Hub_v2` | `"No migrations were applied."` |
| 0f | Build solution | `dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo` | `0 Error(s)` |
| 0g | Run unit tests | `dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.Tests --nologo` | All pass |
| 0h | Start the app | `$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --project AI_Study_Hub_v2 --no-launch-profile --urls http://localhost:5240` | App starts, no errors in log |

### Expected startup log lines

```
Ollama health check passed. BaseUrl=http://localhost:11434, Model=all-minilm:l6-v2
Now listening on: http://localhost:5240
```

---

## 🟪 Phase 1 — Real Embedding (Ollama all-minilm:l6-v2)

**Scope**: Replace FNV-1a hash-based `FakeEmbeddingService` with real `OllamaEmbeddingService` (384-dim vectors via all-minilm:l6-v2). Fault-tolerant per-chunk embedding with skip-and-continue on failure.

### 1.1 Health Check

| # | Test | Expected |
|---|------|----------|
| 1.1 | Check startup log for Ollama health | Contains `"Ollama health check passed"` |

### 1.2 Upload Document & Embedding

| # | Test | Expected |
|---|------|----------|
| 1.2a | Navigate to `http://localhost:5240/documents/upload` | Upload form loads |
| 1.2b | Upload a PDF file (2+ pages with text content) | Snackbar: `"Uploaded 1 file(s)."` |
| 1.2c | Go to Document Library (`/documents`) | Document shows status `Ready` |
| 1.2d | Query embedding_model in DB: | Results show `all-minilm:l6-v2` for all rows |
| | `docker exec supabase-db psql -U postgres -c "SELECT count(*), embedding_model FROM document_chunks GROUP BY embedding_model;"` | |
| 1.2e | Verify vector dimension: | Returns `384` |
| | `docker exec supabase-db psql -U postgres -c "SELECT cardinality(embedding) FROM document_chunks LIMIT 1;"` | |
| 1.2f | Upload a .docx file | Likewise creates chunks with `all-minilm:l6-v2` |
| 1.2g | Upload a .pptx file | Likewise creates chunks with `all-minilm:l6-v2` |

### 1.3 Chat with Document (RAG Quality)

| # | Test | Expected |
|---|------|----------|
| 1.3a | Open Chat (`/chat`), select the uploaded document | Chat interface loads |
| 1.3b | Ask a question related to the document content | AI answers with meaningful content, citing chunks |
| 1.3c | Ask a question NOT in the document | AI responds "not found in the provided documents" |
| 1.3d | Check search scores in server log | Cosine distance scores are meaningful (not random) |

### 1.4 Fault Tolerance

| # | Test | Expected |
|---|------|----------|
| 1.4a | Stop Ollama: `docker stop aistudy-ollama` | App continues running |
| 1.4b | Upload a document with Ollama stopped | File stored successfully (201), but chunks fail |
| 1.4c | Check document status | `Failed` with error message |
| 1.4d | Start Ollama again: `docker start aistudy-ollama` | Container starts, model loads |
| 1.4e | Re-ingest the failed document (see 1.5) | Status changes to `Ready` |

### 1.5 Admin Re-ingest Endpoint

| # | Test | Command | Expected |
|---|------|---------|----------|
| 1.5a | Call re-ingest API | `POST http://localhost:5240/api/documents/{id}/ingest` | Returns `DocumentIngestionResult` with `Success: true` |

> **Note**: Requires Bearer token. Get it from browser DevTools → Application → Local Storage → `sb-*-auth-token`.

---

## 🟦 Phase 2 — Semantic Chunking

**Scope**: Replace naive fixed-size chunking with semantic chunking pipeline: `BlockParser` → `SentenceSplitter` → `ChunkMerger`. Configurable via `RagOptions.ChunkingStrategy`.

### 2.1 Verify Default Configuration

| # | Test | Expected |
|---|------|----------|
| 2.1a | Check `appsettings.Development.json` | `Rag:ChunkingStrategy` = `"semantic"` |
| 2.1b | Check `RagOptions.cs` defaults | `MinChunkChars = 100`, `MaxSectionChars = 1000` |
| 2.1c | Confirm `Program.cs` injects `ChunkingService` (not `FixedSizeChunkingService`) when strategy = `"semantic"` | See `Program.cs:133-138` |

### 2.2 Semantic Chunk Quality

| # | Test | Expected |
|---|------|----------|
| 2.2a | Upload a structured PDF (headings, paragraphs, bullet lists, code blocks) | Ingestion completes with `Ready` status |
| 2.2b | Inspect chunks in DB: `SELECT chunk_index, page_number, length(content) as len, substring(content, 1, 80) as preview FROM document_chunks WHERE document_id = '...' ORDER BY chunk_index;` | Each chunk is a coherent semantic block |
| 2.2c | No chunk cuts mid-heading | Heading is at start of its chunk |
| 2.2d | No chunk cuts mid-paragraph | Each paragraph belongs to one chunk |
| 2.2e | Code blocks are kept intact (not split) | Code block is wholly within one chunk |
| 2.2f | No chunk shorter than `MinChunkChars` (100) | All chunks >= 100 chars |
| 2.2g | No chunk exceeds `MaxSectionChars` (1000) | All chunks <= 1000 chars |

### 2.3 Chunking Benchmark Comparison

| # | Test | Command | Expected |
|---|------|---------|----------|
| 2.3a | Run chunking benchmark | `POST http://localhost:5240/api/benchmark/chunking-compare` | JSON response with `Semantic.RecallAtK` and `Fixed.RecallAtK` |
| 2.3b | Compare recall@K | Inspect response values | `Semantic.RecallAtK` ≥ `Fixed.RecallAtK` |

### 2.4 Fixed-Size Fallback (Comparison)

| # | Test | Expected |
|---|------|----------|
| 2.4a | Edit `appsettings.Development.json`: set `"ChunkingStrategy": "fixed"` | — |
| 2.4b | Restart app | — |
| 2.4c | Upload the same structured PDF | Chunks are cut at 500 chars with 200 overlap |
| 2.4d | Inspect chunks: some may cut mid-sentence or mid-paragraph | Natural boundaries ignored |
| 2.4e | **Revert**: set `"ChunkingStrategy": "semantic"` | Restart app |

---

## 🟫 Phase 3 — RAG Quality

**Scope**: Hybrid search (vector + keyword), ReRankService, CachingEmbeddingService, BenchmarkAutomationHostedService, Admin Benchmark UI.

### 3.1 Configuration

| # | Test | Expected |
|---|------|----------|
| 3.1a | Check `appsettings.Development.json` | All Phase 3 settings present (see [config reference](#-configuration-reference)) |
| 3.1b | Migration applied | `dotnet ef migrations list` includes `20260702123000_AddPhase3BenchmarkHistoryAndKeywordIndex` |

### 3.2 Embedding Cache

| # | Test | Expected |
|---|------|----------|
| 3.2a | Chat with a document (first query) | Embedding calls Ollama — log shows `POST /api/embed` |
| 3.2b | Ask a very similar query | Embedding served from cache — no `POST /api/embed` in log |
| 3.2c | Wait 30+ minutes (or reduce `EmbeddingCacheTtlMinutes` to 1 in config) | Cache expires, next query calls Ollama again |

### 3.3 Hybrid Search (Vector + Keyword)

| # | Test | Expected |
|---|------|----------|
| 3.3a | Verify config | `SearchMode = "hybrid"`, `HybridSearchEnabled = true`, `VectorWeight = 0.7` |
| 3.3b | Chat with document using specific keywords (names, technical terms) | Search results include keyword-matched chunks even if vector similarity is low |
| 3.3c | Toggle to vector-only: set `"SearchMode": "vector"`, restart | Chat results differ (keyword matching disabled) |
| 3.3d | Toggle back to hybrid: set `"SearchMode": "hybrid"`, restart | — |

### 3.4 Re-Ranking

| # | Test | Expected |
|---|------|----------|
| 3.4a | Verify config | `ReRankEnabled = true`, `ReRankCandidateCount = 20`, `ReRankTopN = 5` |
| 3.4b | Chat with a document that has many chunks (20+) | Top 5 results are re-ranked (order differs from raw vector similarity) |
| 3.4c | Toggle re-rank off: set `"ReRankEnabled": false`, restart | Chat results are different (no re-ranking applied) |
| 3.4d | Toggle re-rank back on | — |

### 3.5 Benchmark Automation

| # | Test | Expected |
|---|------|----------|
| 3.5a | Default config | `BenchmarkAutomationEnabled = false` (disabled) |
| 3.5b | Enable automation: set `"BenchmarkAutomationEnabled": true`, restart | Log shows benchmark automation registered |
| 3.5c | Navigate to `http://localhost:5240/admin/benchmarks` | Admin page shows benchmark history (if any runs completed) |
| 3.5d | Check DB: `SELECT * FROM benchmark_run_records ORDER BY completed_at DESC;` | Shows completed runs (if automation interval triggered) |

### 3.6 Manual Benchmark API

| # | Test | Command | Expected |
|---|------|---------|----------|
| 3.6a | Run manual benchmark | `POST http://localhost:5240/api/benchmark/run` | Returns `BenchmarkResult` JSON |
| 3.6b | Verify DB persistence | `SELECT * FROM benchmark_run_records ORDER BY created_at DESC LIMIT 1;` | Latest run is recorded |

---

## ✅ Full Integration Smoke Test

Run these end-to-end scenarios.

| # | Scenario | Steps | Expected |
|---|----------|-------|----------|
| S1 | **Upload 3 file types** | Upload 1 PDF, 1 DOCX, 1 TXT via `/documents/upload` | All 3 show `Ready` in library |
| S2 | **Chat with documents** | Open chat, select each document, ask content-related questions | Each returns meaningful answers |
| S3 | **Re-index a document** | Use `POST /api/documents/{id}/ingest` | Ingestion re-runs; document stays `Ready` |
| S4 | **Upload empty/scan document** | Upload a scanned PDF (no extractable text, just images) | Document status → `Failed`, error: "No extractable text found" |
| S5 | **Upload with Ollama offline** | `docker stop aistudy-ollama`, upload a PDF | File stored (201), chunks fail → status `Failed` |
| S6 | **Re-index after Ollama back** | `docker start aistudy-ollama`, wait for model, `POST /api/documents/{id}/ingest` | Status → `Ready` |
| S7 | **Run chunking benchmark** | `POST /api/benchmark/chunking-compare` | Returns comparison, `Semantic.RecallAtK` >= `Fixed.RecallAtK` |
| S8 | **Run chat benchmark** | `POST /api/benchmark/run` | Returns benchmark result with score |
| S9 | **Toggle search modes** | Chat with hybrid → chat with vector-only → chat with re-rank off | Observe different result sets |
| S10 | **Full build + test** | `dotnet build --nologo` + `dotnet test --nologo` | Build: `0 Error(s)`, Tests: all pass |

---

## 📁 File Reference

### Phase 1 — Real Embedding

| File | Description |
|------|-------------|
| `Services/Rag/OllamaEmbeddingService.cs` | Ollama `/api/embed` client with exponential backoff retry |
| `Services/OllamaHealthCheck.cs` | Startup connectivity check (warn-only, does not block) |
| `Options/OllamaOptions.cs` | `BaseUrl`, `Model`, `TimeoutSeconds`, `MaxRetries` |
| `Services/Rag/DocumentIngestionService.cs` | Fault-tolerant per-chunk embedding (skip + log on failure) |
| `Services/Rag/RagSearchService.cs` | EmbeddingModel filter for cross-model safety |
| `Data/Entities/DocumentChunk.cs` | Added `EmbeddingModel` column |
| `Migrations/20260701132803_AddEmbeddingModelToDocumentChunks.cs` | Schema: column + partial IVFFlat index |
| `Controllers/AdminDocumentsController.cs` | Admin re-ingest endpoint |
| `infra/ollama/docker-compose.yml` | Standalone Ollama Docker (shares supabase_network) |

### Phase 2 — Semantic Chunking

| File | Description |
|------|-------------|
| `Services/Rag/BlockParser.cs` | Parse document structure: headings, paragraphs, lists, code blocks |
| `Services/Rag/SentenceSplitter.cs` | Sentence-level boundary detection |
| `Services/Rag/ChunkMerger.cs` | Merge semantic blocks into chunks respecting size limits |
| `Services/Rag/FixedSizeChunkingService.cs` | Fallback: fixed-size chunking (500 char, 200 overlap) |
| `Services/Rag/ChunkingService.cs` | Refactored: delegates to BlockParser → SentenceSplitter → ChunkMerger |
| `Services/Rag/SemanticChunkingModels.cs` | Data models: `BlockType`, `TextBlock`, `ChunkingContext` |
| `Services/Rag/Benchmarking/ChunkingBenchmarkService.cs` | Compare semantic vs fixed chunking recall@K |
| `Services/Rag/Benchmarking/ChunkingBenchmarkDataset.cs` | Dataset for benchmark comparisons |
| `Controllers/BenchmarkController.cs` | `POST /api/benchmark/chunking-compare` |
| `docs/RBL_PHASE2_TEST_GUIDE.md` | Phase 2 test documentation |

### Phase 3 — RAG Quality

| File | Description |
|------|-------------|
| `Services/Rag/ReRankService.cs` | Cross-encoder re-ranking of search candidates |
| `Services/Rag/CachingEmbeddingService.cs` | Embedding cache with TTL + LRU eviction |
| `Services/Rag/RagSearchService.cs` | Enhanced: hybrid search (vector + keyword), configurable weights |
| `Services/Rag/Benchmarking/BenchmarkRunner.cs` | Full benchmark pipeline (search → LLM judge → score) |
| `Services/Rag/Benchmarking/BenchmarkAutomationHostedService.cs` | Scheduled benchmark runs |
| `Services/BenchmarkApiClient.cs` | HTTP client for benchmark API |
| `Data/Entities/BenchmarkRunRecord.cs` | Entity for persisting benchmark results |
| `Data/Configurations/BenchmarkRunRecordConfiguration.cs` | EF Core configuration |
| `Migrations/20260702123000_AddPhase3BenchmarkHistoryAndKeywordIndex.cs` | Schema: benchmark_run_records + keyword index |
| `Components/Admin/Benchmarks/Benchmarks.razor` | Admin UI for viewing benchmark history |
| `Controllers/BenchmarkController.cs` | `POST /api/benchmark/run`, `POST /api/benchmark/chunking-compare` |
| `Options/RagOptions.cs` | Phase 3 config: cache, re-rank, hybrid, benchmark |
| `docs/RBL_PHASE3_TEST_GUIDE.md` | Phase 3 test documentation |

---

## ⚙️ Configuration Reference

All settings in `appsettings.Development.json` under the `"Rag"` section.

| Key | Phase | Default | Values | Description |
|-----|-------|---------|--------|-------------|
| `ChunkingStrategy` | P2 | `"semantic"` | `"semantic"`, `"fixed"` | Chunking method |
| `ChunkSizeChars` | P1 | `500` | int | Target chunk size (fixed mode) |
| `ChunkOverlapChars` | P1 | `200` | int | Overlap between chunks (fixed mode) |
| `MinChunkChars` | P2 | `100` | int | Minimum chunk length |
| `MaxSectionChars` | P2 | `1000` | int | Maximum chunk length |
| `DefaultTopK` | P1 | `5` | int | Default number of search results |
| `MaxTopK` | P1 | `10` | int | Maximum search results allowed |
| `EmbeddingDimensions` | P1 | `384` | int | Vector dimension |
| `MaxContextChars` | P1 | `6000` | int | Max context for LLM prompt |
| `EmbeddingCacheEnabled` | P3 | `true` | bool | Cache embedding results |
| `EmbeddingCacheMaxEntries` | P3 | `1000` | int | LRU cache size |
| `EmbeddingCacheTtlMinutes` | P3 | `30` | int | Cache TTL |
| `ReRankEnabled` | P3 | `true` | bool | Enable cross-encoder re-ranking |
| `ReRankCandidateCount` | P3 | `20` | int | Candidates to fetch before re-rank |
| `ReRankTopN` | P3 | `5` | int | Results after re-rank |
| `HybridSearchEnabled` | P3 | `true` | bool | Enable hybrid vector + keyword search |
| `VectorWeight` | P3 | `0.7` | 0.0–1.0 | Weight for vector score (vs keyword) |
| `SearchMode` | P3 | `"hybrid"` | `"hybrid"`, `"vector"` | Search algorithm |
| `BenchmarkAutomationEnabled` | P3 | `false` | bool | Auto-run benchmarks |
| `BenchmarkAutomationIntervalHours` | P3 | `168` | int | Interval between auto-runs (7 days) |
| `BenchmarkAlertDropPercent` | P3 | `10` | % | Alert if score drops this much |

### Ollama Settings (under `"Ollama"`)

| Key | Phase | Default | Description |
|-----|-------|---------|-------------|
| `BaseUrl` | P1 | `http://localhost:11434` | Ollama server URL |
| `Model` | P1 | `all-minilm:l6-v2` | Embedding model |
| `TimeoutSeconds` | P1 | `30` | Per-request timeout |
| `MaxRetries` | P1 | `3` | Retry attempts (exponential backoff) |

---

## 🔧 Appendix: Useful Commands

### Docker

```powershell
# Start / stop Ollama
docker compose -f infra\ollama\docker-compose.yml up -d
docker compose -f infra\ollama\docker-compose.yml down

# Check Ollama logs
docker logs aistudy-ollama

# Test Ollama embedding API
curl.exe -X POST http://localhost:11434/api/embed -H "Content-Type: application/json" -d '{\"model\":\"all-minilm:l6-v2\",\"input\":\"Test sentence\"}'

# List models
curl.exe -s http://localhost:11434/api/tags
```

### Database

```powershell
# Query document chunks
docker exec supabase-db psql -U postgres -c "SELECT d.file_name, COUNT(dc.id) as chunk_count FROM documents d LEFT JOIN document_chunks dc ON dc.document_id = d.id GROUP BY d.id, d.file_name ORDER BY d.created_at DESC LIMIT 10;"

# Check embedding model
docker exec supabase-db psql -U postgres -c "SELECT embedding_model, count(*) FROM document_chunks GROUP BY embedding_model;"

# Check benchmark records
docker exec supabase-db psql -U postgres -c "SELECT * FROM benchmark_run_records ORDER BY created_at DESC LIMIT 5;"

# Apply migrations
dotnet ef database update --project AI_Study_Hub_v2
```

### API

```powershell
# Re-ingest a document (requires auth token)
$token = "<your-bearer-token>"
$docId = "<document-guid>"
curl.exe -X POST "http://localhost:5240/api/documents/$docId/ingest" -H "Authorization: Bearer $token" -H "Content-Type: application/json"

# Run chunking benchmark
curl.exe -X POST "http://localhost:5240/api/benchmark/chunking-compare" -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d "{}"

# Run full benchmark
curl.exe -X POST "http://localhost:5240/api/benchmark/run" -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d "{}"
```

### Get Auth Token

Open browser DevTools (F12) → Application → Local Storage → `http://localhost:5240` → Look for key `sb-*-auth-token` → Find `"access_token"` value.

---

## ⚠️ Known Issues / Notes

| Issue | Description | Workaround |
|-------|-------------|------------|
| **Partial ingestion silent** | If some chunks fail embedding but others succeed, document shows `Ready` — user not warned | Check app logs for "Skipping chunk" warnings |
| **Upload page doesn't show ingestion failure** | Upload API returns 201 even if ingestion fails | User must check Document Library for `Failed` status |
| **No auto-retry** | Failed chunks are skipped, never retried automatically | Use `POST /api/documents/{id}/ingest` to retry |
| **Benchmark automation** | Disabled by default (`BenchmarkAutomationEnabled: false`) | Enable manually for testing; don't enable in production without monitoring |
| **CachingEmbeddingService** | Caches by exact input string — semantically similar but different strings miss cache | Acceptable trade-off; TTL eviction handles staleness |
| **ReRankService** | Uses cross-encoder model from Ollama — may increase latency | `ReRankCandidateCount=20` finds good balance |
