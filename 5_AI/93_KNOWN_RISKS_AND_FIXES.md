# Sprint 2 RAG Known Risks And Fixes

This file tracks QA/integration risks found from the contract baseline in `feature/s2-qa-docs` and the Sprint 2 plan. Update it after each worker branch merge with real evidence only.

## Verified Baseline

- Branch: `feature/s2-qa-docs`.
- Current commit at session start: `9c243c1 docs(rag): add sprint 2 smoke guidance`.
- Worktree before edits: clean.
- Build evidence before QA edits: `dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo` passed with 0 warnings / 0 errors.
- Test evidence before QA edits: `dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo --no-build` passed with 105 passed, 1 skipped, 0 failed.
- Runtime smoke: not executed because `infra/supabase/.env` is absent in this QA worktree, ports `5432`/`8000`/`5240` were not listening, and compose emitted missing environment variable warnings.
- Secret key names present in user-secrets without printing values: Supabase JWT/keys, Postgres connection string, default admin password, and `Groq:ApiKey`.

## Integration Blockers On This Branch

| Blocker | Impact | Recommended Fix / Owner |
|---|---|---|
| No ingestion implementation is merged | Upload does not yet prove extract/chunk/embed behavior. | Merge AI 1 ingestion branch; ensure upload triggers ingestion or add a documented manual `POST /api/documents/{id}/ingest`. |
| No embedding/search implementation is merged | `POST /api/rag/search` does not exist; cannot validate top-K or owner-only retrieval. | Merge AI 2 embedding/search branch; keep `RagSearchRequest` / `RagSearchResultDto` contract unless coordinator updates all callers. |
| No chat API implementation is merged | `POST /api/ai/chat/ask` does not exist; cannot validate grounded answer, no-sources behavior, or Groq error behavior. | Merge AI 3 chat API branch; map errors to controlled API responses, not unhandled 500s. |
| No chat UI is merged | `/ai/chat` route and nav entry do not exist; browser demo cannot be completed. | Merge AI 4 UI branch after API route is stable. |
| Supabase `.env` is missing in QA worktree | Runtime smoke cannot start local stack from this branch without setup. | Run `setup.ps1` in this worktree or recreate local env through the approved setup flow; never commit `.env`. |

## High-Risk Merge Files

Watch these during integration because multiple Sprint 2 branches are likely to touch them:

- `AI_Study_Hub_v2/Program.cs`: DI registrations for ingestion, embedding, search, chat service, typed UI clients, Semantic Kernel/Groq HTTP client.
- `AI_Study_Hub_v2/AI_Study_Hub_v2.csproj`: NuGet packages for PDF parsing, embeddings, Semantic Kernel, or ONNX.
- `AI_Study_Hub_v2/appsettings.json` and `AI_Study_Hub_v2/appsettings.Development.json`: options sections only; do not add secrets.
- `AI_Study_Hub_v2/Dtos/AiChatDtos.cs` and `AI_Study_Hub_v2/Dtos/RagDtos.cs`: route contract compatibility.
- `AI_Study_Hub_v2/Data/AppDbContext.cs` and `Migrations/*`: document chunk mapping, pgvector support, optional status enum changes.
- `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Support/TestDb.cs`: in-memory tests currently cannot cover provider-specific pgvector SQL.
- `AI_Study_Hub_v2/Components/Layout/NavMenu.razor`: UI branch likely adds `/ai/chat` navigation.

## Contract Compatibility Checks

| Topic | Current Contract / Baseline | Risk | Recommended Fix |
|---|---|---|---|
| Embedding dimension | `DocumentChunk.EmbeddingDimension = 384`, `RagOptions.EmbeddingDimensions = 384`, DB column `vector(384)`. | Any model returning non-384 vectors will fail persistence/search. | Validate embedding length before save and fail with clear error. Do not change dimension without migration and re-ingestion plan. |
| Chunk index | Entity comment says 0-based. | UI/API may display 0 to users or another branch may assume 1-based. | Store 0-based; display `chunkIndex + 1` only in UI labels if needed. |
| Page number | Nullable `int?`, intended 1-based if known. | PDF extractor may not provide pages; UI must handle null. | Render `Page unknown` or omit page rather than showing `0`. |
| Status names | Baseline enum has `Uploading`, `Ready`, `Processing`, `Failed`; `DocumentService.UploadAsync` currently saves uploaded docs as `Ready`. | Search/UI may disagree whether `Ready` means stored-only or RAG-indexed. | Coordinator should lock semantics. QA recommends: `Ready` = stored and queued/available for ingestion, `Processing` = ingestion running, `Failed` = unusable; if an `Indexed` state is required, add exactly one migration and update all branches. |
| TopK | Request default `5`, `RagOptions.MaxTopK = 10`. | Large values can blow prompt budget and slow search. | Clamp or reject invalid `TopK`; test `0`, negative, and above max. |
| Owner filtering | RAG service receives Supabase auth user id; `documents.user_id` stores internal `public.users.id`. | Searching by `documentId`, folder, subject, or semester can leak another user's chunks if filters are wrong. | Resolve `auth.users.id` to `public.users.id`, join chunks through documents, filter owner before top-K ordering, and return no-leak 404/empty results. |
| Groq secret | `Groq:ApiKey` should come from user-secrets. | Accidental appsettings secret or missing key can break demo. | Validate key only where chat is used; return clear configuration error in Development. Never commit key. |
| Test DB | InMemory support can cover DTO/options/helpers, not pgvector ranking. | Unit tests may pass while real SQL fails. | Use service-level fakes for unit tests; add manual Postgres smoke for vector SQL. |

## Fix Playbook

### Risk 1: Workers conflict in shared files

Symptoms:

- Git merge conflicts in `Program.cs`, `.csproj`, DTOs, `NavMenu.razor`, `TestDb.cs`, migrations.

Fix:

- Keep one implementation per interface.
- Prefer contract DTOs from `02_CONTRACT_FIRST_PLAN.md` unless coordinator intentionally updates the contract.
- Move duplicate service code into one canonical file.
- Do not keep both fake and real runtime services registered unless selected by config.

### Risk 2: No chunks after upload

Symptoms:

- Chat API returns `no_sources`.
- DB `select count(*) from public.document_chunks` returns 0 for the uploaded document.

Fix:

- Check whether upload calls `IDocumentIngestionService` or whether a background/manual ingest endpoint is required.
- Check `ITextExtractionService` returned non-empty pages.
- Check `IChunkingService` output and save path.
- Check status filters: retrieval may be excluding `Ready` or `Processing` documents.
- Add a manual re-ingest endpoint for smoke if automatic trigger is not demo-ready.

### Risk 3: Embedding dimension mismatch

Symptoms:

- DB insert fails.
- pgvector error mentions dimensions.
- Search SQL fails on vector distance.

Fix:

- Ensure every embedding returns exactly 384 floats.
- Check `DocumentChunk.EmbeddingDimension`, `RagOptions.EmbeddingDimensions`, and DB `vector(384)` match.
- Do not switch to 768/1024/1536-dim embeddings without migration and re-ingestion.

### Risk 4: pgvector SQL fails

Symptoms:

- Unit tests pass but `POST /api/rag/search` fails in Postgres.
- Errors mention missing `vector` extension, operator class, casts, or SQL translation.

Fix:

- Confirm migration enabled `vector` extension and created `ix_document_chunks_embedding`.
- Confirm `npgsql.UseVector()` remains configured in `Program.cs`.
- Prefer parameterized raw SQL for vector distance if EF cannot translate it reliably.
- Run the DB smoke query against local Postgres before demo.

### Risk 5: Groq quota/error

Symptoms:

- 401 unauthorized, 429 quota/rate limit, 503/provider timeout, or long response time.

Fix:

- Verify `Groq:ApiKey` exists in user-secrets; never print or commit the key.
- Use default model `llama-3.1-8b-instant`.
- Reduce `TopK` or `MaxContextChars` to lower prompt size.
- Return controlled provider error/fallback to API and UI.
- Demo `/api/rag/search` as backup if generation is down.

### Risk 6: Hallucination or `no_sources`

Symptoms:

- Unrelated question gets a confident answer.
- `sources` is empty but the answer claims facts.
- Citations do not correspond to answer text.

Fix:

- Chat service should not call Groq when no sources are retrieved if no-source fallback is the selected behavior.
- Prompt must say answer only from source excerpts and use `[S1]`, `[S2]` markers.
- UI should show `refusalReason` or a clear fallback state.
- Add smoke question: `Does the document mention quantum physics?`

### Risk 7: Owner data leak

Symptoms:

- Search returns chunks from another user.
- User A can query User B document id or folder id.

Fix:

- Always map Supabase `sub` to `public.users.id`.
- Join `document_chunks -> documents -> users` and filter owner before ranking.
- Add two-user smoke and tests for document id, folder id, subject, and semester filters.

### Risk 8: Status naming mismatch

Symptoms:

- Ingestion marks `Ready` but search expects `Indexed`.
- UI says not indexed even chunks exist.
- Upload flow leaves documents stuck in `Processing` or `Ready` forever.

Fix:

- Coordinator locks one convention before merging implementation branches.
- Update ingestion, search, UI, tests, and docs consistently.
- Avoid adding enum values in multiple branches.
- If adding `Indexed`, use one migration only.

### Risk 9: PDF scan extraction fail

Symptoms:

- Extracted text is empty.
- `chunk_count = 0`.
- Answer fallback triggers for every question.

Fix:

- Use text-based PDF for Sprint 2 demo.
- Mark scanned/unreadable PDF as failed or show a controlled extraction error.
- State OCR is out of scope for Sprint 2.

### Risk 10: Delete cleanup incomplete

Symptoms:

- Deleted document is gone from `/documents` but chunks still appear in RAG search.
- DB still has chunks for deleted document.

Fix:

- Verify FK cascade from `documents` to `document_chunks` is active.
- If soft delete is introduced later, filter deleted/inactive docs before retrieval.
- Add cleanup smoke query and retrieval-after-delete check.

## Recommended Merge Order

1. Merge AI 1 ingestion first so uploads can produce chunks/status evidence.
2. Merge AI 2 embedding/search next and verify owner-only top-K retrieval before LLM work.
3. Merge AI 3 chat API after retrieval contract is stable.
4. Merge AI 4 chat UI after `POST /api/ai/chat/ask` response shape is stable.
5. Merge AI 5 QA/docs after implementation branches, then re-run this smoke checklist.

## Focused Tests To Add After Feature Merge

Add these only after implementation exists; do not fake green tests against missing features.

| Area | Test |
|---|---|
| Ingestion | Text-based PDF extracts at least one page and creates deterministic chunk order. |
| Ingestion failure | Unsupported/scanned/unreadable file marks document failed or returns controlled error. |
| Embedding | Service rejects vector length not equal to 384 before DB save. |
| Search | `TopK` is clamped or rejected consistently. |
| Search security | User A cannot retrieve User B chunks by document id, folder id, subject, or semester. |
| Chat API | No retrieved sources returns refusal/fallback without Groq call if designed that way. |
| Chat API | Groq timeout/rate limit maps to stable error or fallback response. |
| UI | Chat page renders loading, answer, source list, and error/refusal state. |
| Cleanup | Deleting a document removes chunks or excludes them from future search. |

## Integration Priority If Time Is Short

If deadline is close, preserve this vertical slice:

1. Text-based PDF extraction.
2. Simple chunking.
3. Deterministic 384-dim fake embeddings if real embedding is blocked.
4. Vector or lexical fallback retrieval with owner filters.
5. Chat API with source citations and no-source fallback.
6. Minimal `/ai/chat` UI.

Do not spend demo-critical time on chat history, exact PDF highlights, OCR, admin quota dashboards, or advanced semantic chunking.
