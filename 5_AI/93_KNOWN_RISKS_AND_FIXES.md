# Sprint 2 RAG Known Risks And Fixes

This file tracks QA/integration risks found from the contract baseline in `feature/s2-qa-docs` and the Sprint 2 plan. It should be updated after worker branches are merged.

## Verified Baseline

- Branch: `feature/s2-qa-docs`.
- Base commit: `ccbaedd feat(rag): add sprint 2 shared contracts`.
- Worktree before docs: clean.
- Build evidence: `dotnet build AI_Study_Hub_v2.sln --nologo` passed with 0 warnings / 0 errors.
- Test evidence: `dotnet test AI_Study_Hub_v2.sln --nologo` passed with 105 passed, 1 skipped, 0 failed.
- Runtime smoke: not executed because `infra/supabase/.env` is absent in this QA worktree and Supabase services were not listening on `5432`/`8000`.

## Integration Blockers On This Branch

| Blocker | Impact | Recommended Fix / Owner |
|---|---|---|
| No ingestion implementation is merged | Upload does not yet prove extract/chunk/embed behavior. | Merge AI 1 ingestion branch; ensure upload triggers ingestion or add a documented manual `POST /api/documents/{id}/ingest`. |
| No embedding/search implementation is merged | `POST /api/rag/search` does not exist; cannot validate top-K or owner-only retrieval. | Merge AI 2 embedding/search branch; keep `RagSearchRequest` / `RagSearchResultDto` contract unchanged unless coordinator updates all users. |
| No chat API implementation is merged | `POST /api/ai/chat/ask` does not exist; cannot validate grounded answer or Groq error behavior. | Merge AI 3 chat API branch; map errors to controlled API responses, not unhandled 500s. |
| No chat UI is merged | `/ai/chat` route and nav entry do not exist; browser demo cannot be completed. | Merge AI 4 UI branch after API route is stable. |
| Supabase `.env` is missing in QA worktree | Runtime smoke cannot start local stack from this branch without setup. | Run `setup.ps1` in this worktree or copy/recreate local env through approved setup flow; never commit `.env`. |

## High-Risk Merge Files

Watch these during integration because multiple Sprint 2 branches are likely to touch them:

- `AI_Study_Hub_v2/Program.cs`: DI registrations for ingestion, embedding, search, chat service, typed UI clients, Semantic Kernel/Groq HTTP client.
- `AI_Study_Hub_v2/AI_Study_Hub_v2.csproj`: NuGet packages for PDF parsing, embeddings, Semantic Kernel, or ONNX.
- `AI_Study_Hub_v2/appsettings.json` and `AI_Study_Hub_v2/appsettings.Development.json`: options sections only; do not add secrets.
- `AI_Study_Hub_v2/Dtos/AiChatDtos.cs` and `AI_Study_Hub_v2/Dtos/RagDtos.cs`: route contract compatibility.
- `AI_Study_Hub_v2/Data/AppDbContext.cs` and `Migrations/*`: document chunk mapping, pgvector support, optional status enum changes.
- `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Support/TestDb.cs`: in-memory tests currently ignore `DocumentChunk` because pgvector is provider-specific.
- `AI_Study_Hub_v2/Components/Layout/NavMenu.razor`: UI branch likely adds `/ai/chat` navigation.

## Contract Compatibility Checks

| Topic | Current Contract / Baseline | Risk | Recommended Fix |
|---|---|---|---|
| Embedding dimension | `DocumentChunk.EmbeddingDimension = 384`, `RagOptions.EmbeddingDimensions = 384`, DB column `vector(384)`. | Any model returning non-384 vectors will fail persistence/search. | Validate embedding length before save and fail with clear error. Do not change dimension without migration and re-ingestion plan. |
| Chunk index | Entity comment says 0-based. | UI/API may display 0 to users or another branch may assume 1-based. | Store 0-based; display `chunkIndex + 1` only in UI labels if needed. |
| Page number | Nullable `int?`, intended 1-based if known. | PDF extractor may not provide pages; UI must handle null. | Render `Page unknown` or omit page rather than showing `0`. |
| Status names | Baseline enum likely has `Uploading`, `Ready`, `Failed`. | Branches may introduce `Uploaded`, `Processing`, `Indexed` inconsistently. | Coordinator should pick one status convention before merging migrations. QA recommends minimal: `Ready` means stored and RAG-indexed for Sprint 2, or migrate once to `Uploaded`/`Processing`/`Indexed`/`Failed`. |
| TopK | Request default `5`, `RagOptions.MaxTopK = 10`. | Large values can blow prompt budget and slow search. | Clamp or reject invalid `TopK`; test `0`, negative, and above max. |
| Owner filtering | Services receive Supabase auth user id. Documents use internal `public.users.id`. | Searching by `documentId` can leak another user's chunks if filter joins are wrong. | Always resolve `auth.users.id` to `public.users.id`, join through `documents.user_id`, and return no-leak 404/empty results. |
| Groq secret | `Groq:ApiKey` should come from user-secrets. | Accidental appsettings secret or missing key can break demo. | Validate key only where chat is used; return clear configuration error in Development. Never commit key. |
| Test DB | InMemory support ignores `DocumentChunk`. | Search service tests using EF InMemory cannot cover pgvector queries. | Use service-level fakes for unit tests; add provider/integration test only when local Postgres is part of CI/manual smoke. |

## Recommended Merge Order

1. Merge AI 1 ingestion first so uploads can produce chunks/status evidence.
2. Merge AI 2 embedding/search next and verify owner-only top-K retrieval before LLM work.
3. Merge AI 3 chat API after retrieval contract is stable.
4. Merge AI 4 chat UI after `POST /api/ai/chat/ask` response shape is stable.
5. Re-run AI 5 smoke checklist after each merge, especially build/test and DB status/chunk checks.

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

## Demo Risks

- Groq free tier can rate-limit or fail; keep `/api/rag/search` evidence ready as backup.
- If PDF parsing only supports text-based PDFs, explicitly say scanned PDFs/OCR are out of scope.
- If ingestion runs async, demo needs a visible processing state or a manual re-ingest endpoint to avoid awkward waiting.
- If the app starts with migrations on launch, a broken migration blocks all UI smoke; run `dotnet build` and migration review before live demo.
- Do not run multiple backend instances on `5240`; stop `dotnet run` after smoke to avoid file locks.

## Fix Patterns

- Missing DI registration: add scoped service registration in `Program.cs`, then run build.
- Route mismatch: keep planned routes `POST /api/rag/search` and `POST /api/ai/chat/ask`; update UI/API clients only after coordinator approval.
- Missing configuration: add non-secret defaults to `appsettings*.json`; set actual secrets through `dotnet user-secrets`.
- EF/pgvector failure: confirm `npgsql.UseVector()` remains in `Program.cs` and `DocumentChunk.Embedding` column type stays `vector(384)`.
- Cross-user result leak: add a join/filter on `documents.user_id` before vector ordering; do not filter only after top-K selection.
