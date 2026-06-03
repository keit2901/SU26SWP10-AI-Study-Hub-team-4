# Sprint 2 RAG Known Risks And Fixes

This file tracks QA/integration risks for the merged `sprint2/integration` branch and the Sprint 2 RAG plan. Update it after runtime smoke with real evidence only.

## Verified Baseline

- Branch: `sprint2/integration`.
- Base contract commit: `ccbaedd feat(rag): add sprint 2 shared contracts`.
- Latest worker commits merged: `3fff2eb`, `91998ac`, `d742aa8`, `b08004c`, `80f5bb9`.
- Build evidence: build passed after each worker merge in the integration worktree.
- Runtime smoke: not executed in this merge session; it still requires local Supabase services and `Groq:ApiKey` in user-secrets/environment.
- Secret handling: do not commit or print Supabase keys, Postgres passwords, default admin password, or `Groq:ApiKey`.

## Remaining Integration Blockers

| Blocker | Impact | Recommended Fix / Owner |
|---|---|---|
| Runtime smoke not executed | End-to-end browser/API evidence is still pending. | Start Supabase, configure user-secrets, run the smoke checklist, then stop the backend. |
| Groq API key not committed by design | Chat generation returns provider-unavailable until `Groq:ApiKey` is set locally. | Set with `dotnet user-secrets set "Groq:ApiKey" "<key>" --project AI_Study_Hub_v2\AI_Study_Hub_v2.csproj`; never commit it. |
| Scanned PDFs/OCR unsupported | Image-only PDFs will fail ingestion with no extractable text. | Use text-based PDFs for Sprint 2 demo or add OCR in a later sprint. |

## High-Risk Merge Files

Watch these during integration because multiple Sprint 2 branches are likely to touch them:

- `AI_Study_Hub_v2/Program.cs`: DI registrations for ingestion, embedding, search, chat service, typed UI clients, Groq HTTP client.
- `AI_Study_Hub_v2/AI_Study_Hub_v2.csproj`: NuGet packages for PDF parsing, embeddings, Semantic Kernel, or ONNX.
- `AI_Study_Hub_v2/appsettings.json` and `AI_Study_Hub_v2/appsettings.Development.json`: options sections only; do not add secrets.
- `AI_Study_Hub_v2/Dtos/AiChatDtos.cs` and `AI_Study_Hub_v2/Dtos/RagDtos.cs`: route contract compatibility.
- `AI_Study_Hub_v2/Data/AppDbContext.cs` and `Migrations/*`: document chunk mapping, pgvector support, optional status enum changes.
- `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Support/TestDb.cs`: in-memory tests cannot fully cover provider-specific pgvector SQL.
- `AI_Study_Hub_v2/Components/Layout/NavMenu.razor`: `/ai/chat` navigation.
- `AI_Study_Hub_v2/Components/Pages/DocumentDetail.razor`: AI chat launch link and document status display.

## Contract Compatibility Checks

| Topic | Current Contract / Baseline | Risk | Recommended Fix |
|---|---|---|---|
| Embedding dimension | `DocumentChunk.EmbeddingDimension = 384`, `RagOptions.EmbeddingDimensions = 384`, DB column `vector(384)`. | Any model returning non-384 vectors will fail persistence/search. | Validate embedding length before save and fail with clear error. Do not change dimension without migration and re-ingestion plan. |
| Chunk index | Entity comment says 0-based. | UI/API may display 0 to users or another branch may assume 1-based. | Store 0-based; display `chunkIndex + 1` only in UI labels if needed. |
| Page number | Nullable `int?`, intended 1-based if known. | PDF extractor may not provide pages; UI must handle null. | Render `Page unknown` or omit page rather than showing `0`. |
| Status names | Enum has `Uploading`, `Ready`, `Processing`, `Failed`; no `Indexed`. | Search/UI can disagree whether `Ready` means stored-only or RAG-usable. | Lock `Ready` as RAG-usable for Sprint 2; update ingestion, search, UI, and tests consistently. |
| TopK | Request default `5`, `RagOptions.MaxTopK = 10`. | Large values can blow prompt budget and slow search. | Clamp or reject invalid `TopK`; test `0`, negative, and above max. |
| Owner filtering | RAG service receives Supabase auth user id; `documents.user_id` stores internal `public.users.id`. | Searching by `documentId`, folder, subject, or semester can leak another user's chunks if filters are wrong. | Resolve auth id to public user id, join chunks through documents, filter owner before top-K ordering, and return no-leak 404/empty results. |
| Groq secret | `Groq:ApiKey` should come from user-secrets/environment. | Accidental appsettings secret or missing key can break demo. | Validate key only where chat is used; return clear configuration error. Never commit key. |
| Test DB | InMemory can cover DTO/options/helpers, not pgvector ranking. | Unit tests may pass while real SQL fails. | Use unit fakes for logic; add manual Postgres smoke for vector SQL. |

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
- Use manual `POST /api/documents/{id}/ingest` for smoke if automatic trigger needs diagnosis.

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
- EF translation fails for vector distance.

Fix:

- Verify `npgsql.UseVector()` remains in `Program.cs`.
- Verify pgvector extension exists and `document_chunks.embedding` is `vector(384)`.
- If EF translation fails, use parameterized raw SQL for vector search only.

### Risk 5: Groq call fails

Symptoms:

- `missing_api_key`, 401, 429, timeout, or provider 5xx.

Fix:

- Verify `Groq:ApiKey` exists in user-secrets/environment.
- Keep non-secret `Groq` defaults in appsettings only.
- Reduce `Rag:MaxContextChars`/`TopK` if prompt is too large.
- Demo retrieval fallback with `/api/rag/search` if provider is unavailable.

### Risk 6: AI hallucinates answer without source

Symptoms:

- It answers unrelated questions.
- Citations appear without matching sources.

Fix:

- Chat service must not call Groq if no sources.
- Prompt must say answer only from source excerpts.
- Require source markers like `[S1]`.
- UI should show refusal reason if no sources.

### Risk 7: User data leakage

Symptoms:

- Search returns chunks from another user.

Fix:

- Always filter by authenticated Supabase user id mapped to `public.users`.
- Join chunks -> documents -> users.
- Add tests with two users and foreign documents.

### Risk 8: Status mismatch

Symptoms:

- Ingestion marks `Ready` but search/UI expects `Indexed`.
- UI says not indexed even chunks exist.

Fix:

- For Sprint 2, lock `Ready` as RAG-usable.
- Keep `Processing` for in-flight ingestion and `Failed` for unusable files.
- Avoid adding enum values in multiple branches.

### Risk 9: PDF extraction fails for scanned PDFs

Symptoms:

- Extracted text is empty.
- Chunks count is 0.

Fix:

- Use text-based PDF for Sprint 2 demo.
- Mark scanned PDF as failed with a clear message.
- OCR is out of scope for Sprint 2.

### Risk 10: Delete cleanup incomplete

Symptoms:

- Deleted document is gone from `/documents` but chunks still appear in RAG search.
- DB still has chunks for deleted document.

Fix:

- Verify FK cascade from `documents` to `document_chunks` is active.
- If soft delete is introduced later, filter deleted/inactive docs before retrieval.
- Add cleanup smoke query and retrieval-after-delete check.

## Merge Order Applied

1. AI 1 ingestion merged first so uploads can produce chunks/status evidence.
2. AI 2 embedding/search merged next to validate owner-only top-K retrieval before LLM work.
3. AI 3 chat API merged after retrieval contracts were stable.
4. AI 4 chat UI merged after `POST /api/ai/chat/ask` response shape was stable.
5. AI 5 QA/docs merged after implementation branches, then build/test verification was rerun.

## Focused Tests To Maintain

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
