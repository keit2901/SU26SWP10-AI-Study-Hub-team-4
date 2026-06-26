# Current Session — 2026-06-20 (continued)

## Completed

### 1. DeepSeek removed — Gemini implemented (first half)
- Deleted `DeepSeekOptions.cs`, `DeepSeekChatCompletionClient.cs`, cleaned DI/UI
- Created `GeminiOptions.cs`, `GeminiChatCompletionClient.cs`, factory routing
- Build 0 errors, 146/147 tests pass
- Gemini smoke test returned HTTP 429 (free tier quota exhausted)

### 2. Architecture Review — Chunking Audit
- Only **recursive character-based chunking** is implemented (1,000 chars, 200 overlap)
- Reviewed: `ChunkingService`, `DocumentIngestionService`, `RagSearchService`, `FakeEmbeddingService`
- Key weakness: **`FakeEmbeddingService`** — keyword feature-hashing, not semantic embeddings
- No heading-aware, semantic, parent-child, or contextual retrieval exists

### 3. Architecture Review — Scoring Audit
- Reviewed: `BenchmarkEvaluator`, `BenchmarkRunner`, `BenchmarkModels`, `BenchmarkDataset`
- **6 of 7 metrics use unreliable keyword heuristics** — high false-positive/negative
- **Only 8 of 60 questions have reference answers** for correctness checking
- Hallucination rate only detects one narrow edge case — dangerously misleading if interpreted broadly

### 4. Qwen3 32B Validation
- **CONFIRMED** available on Groq as `qwen/qwen3-32b`
- Handles citations (`[S1]`), refusals, system prompts correctly
- ~0.4s latency; outputs CoT (`<think>` blocks) before answers
- **No new provider code needed** — works through existing `GroqChatCompletionClient`

### 5. Final Recommendation Report
- **Verdict: NOT READY** for benchmarking
- **P0**: Replace `FakeEmbeddingService` with real embeddings
- **P1**: Add reference answers to remaining 50 questions
- **P1**: Fix evaluation metrics (replace keyword heuristics)
- **Gemini removal scope**: 2 files to delete, 4 files to edit, 1 cosmetic keep
- **Qwen3 32B**: GO as benchmark challenger

### 6. Bug Fix — Wrong stats after delete (Library Page)

Issue: After deleting a document, the stats box still showed the pre-delete count, and the folder card's `DocumentCount` wasn't decremented.

**Already fixed in previous session**: `DeleteDocumentAsync` in `DocumentLibrary.razor` now finds the folder by `doc.FolderId` and decrements `folder.DocumentCount` after successful deletion. The `_totalDocuments` (line 732) computes from `_documents.Count` which is correct after `_documents.Remove(doc)` + `StateHasChanged()`.

### 7. Bug Fix — Supabase Storage files not deleted on document delete (this session)

**Root cause**: Two issues:
1. `SupabaseStorageClient.DeleteAsync` relied on `HttpClient.DefaultRequestHeaders` (set via DI) for auth headers. `DownloadFileAsync` set headers explicitly per-request. DELETE requests through `_http.DeleteAsync()` were not consistently sending auth headers (or the headers were not being honored for the DELETE method).
2. `DocumentService.DeleteAsync` deleted the DB row **before** storage — with best-effort catch. If storage delete failed, the file was orphaned with no retry path.

**Fix**:
- `SupabaseStorageClient.DeleteAsync:50-65`: Now builds `HttpRequestMessage(HttpMethod.Delete, ...)` manually with **explicit** auth headers (`Authorization` + `apikey`), matching `DownloadFileAsync`'s known-working pattern.
- `DocumentService.DeleteAsync:354-362`: Storage delete now runs **first**. If it throws, the exception propagates and the DB row is preserved — **no orphaned storage objects**.

Test `DeleteAsync_StorageDeleteThrows_StillRemovesRow_AndSwallowsException` → renamed to `DeleteAsync_StorageDeleteThrows_DoesNotRemoveRow` and updated to assert exception + row preserved.

Build: 0 errors, 146/147 pass.

## Current Decisions
- **Benchmarking suspended** until embeddings, scoring, and dataset are fixed
- **Gemini to be removed** (dead code, unnecessary provider)
- **Qwen3 32B** will replace Gemini as the challenger (same provider, zero new infrastructure)
- **Priority**: Fix fundamentals first, benchmark later

## Next Immediate Steps (user's choice)
1. **(Highest)** Replace FakeEmbeddingService with real embeddings
2. Remove Gemini code + add Qwen model option
3. Add reference answers to benchmark dataset
4. Fix evaluation metrics
5. Implement heading-aware chunking
