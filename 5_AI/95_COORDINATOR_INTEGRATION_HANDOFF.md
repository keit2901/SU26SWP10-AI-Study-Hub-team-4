# 95 - Coordinator Integration Handoff: Sprint 2 RAG

Created for the next coordinator/runtime-smoke session so it can continue without reading the chat transcript.

## Load This First In Next Session

1. `D:\FPT\summer2026\SWP391\5_AI\00_START_HERE.md`
2. `D:\FPT\summer2026\SWP391\5_AI\01_SHARED_CONTEXT.md`
3. `D:\FPT\summer2026\SWP391\5_AI\02_CONTRACT_FIRST_PLAN.md`
4. `D:\FPT\summer2026\SWP391\5_AI\90_INTEGRATION_MERGE_CHECKLIST.md`
5. `D:\FPT\summer2026\SWP391_parallel\s2_integration\5_AI\91_SPRINT2_SMOKE_CHECKLIST.md`
6. `D:\FPT\summer2026\SWP391_parallel\s2_integration\5_AI\92_DEMO_SCRIPT.md`
7. `D:\FPT\summer2026\SWP391_parallel\s2_integration\5_AI\93_KNOWN_RISKS_AND_FIXES.md`
8. This file: `D:\FPT\summer2026\SWP391_parallel\s2_integration\5_AI\95_COORDINATOR_INTEGRATION_HANDOFF.md`

## Repository And Worktree State

- Repo root: `D:\FPT\summer2026\SWP391`
- Integration worktree: `D:\FPT\summer2026\SWP391_parallel\s2_integration`
- Integration branch: `sprint2/integration`
- Contract base: `ccbaedd feat(rag): add sprint 2 shared contracts`
- Latest known integration commit from coordinator: `80d7f2f fix(rag): dedupe integration DI registrations`
- Do not merge directly into `main`.
- Do not push unless explicitly requested.
- Do not reset/revert/checkout destructive.
- Do not overwrite unrelated dirty files.

Important: after the coordinator handoff was produced, the worktree was observed dirty with unrelated modified files mostly under admin/dashboard UI paths. Treat those as user/other-session changes unless proven otherwise. Do not revert them.

## Worker Branches Merged

All worker branches were merged into `sprint2/integration` using branch merges, not cherry-picks.

| Worker | Branch | Latest commit merged | Merge commit |
|---|---|---|---|
| AI 1 | `feature/s2-ingestion` | `3fff2eb feat(rag): add document ingestion and chunking pipeline` | `e8343f3 merge: sprint 2 ingestion pipeline latest` |
| AI 2 | `feature/s2-embedding-search` | `91998ac feat(rag): add embedding and vector search service` | `526f57f merge: sprint 2 embedding search latest` |
| AI 3 | `feature/s2-chat-api` | `d742aa8 feat(rag): add grounded AI chat API` | `fd5997b merge: sprint 2 chat API latest` |
| AI 4 | `feature/s2-chat-ui` | `b08004c feat(ui): add AI chat page with citations` | `499ef74 merge: sprint 2 chat UI latest` |
| AI 5 | `feature/s2-qa-docs` | `80f5bb9 docs(rag): add sprint 2 QA and demo checklist` | `93dadeb merge: sprint 2 QA docs latest` |

Coordinator cleanup commit:

- `80d7f2f fix(rag): dedupe integration DI registrations`

## Conflicts And Coordinator Resolutions

Conflicts occurred only in QA documentation:

- `5_AI/91_SPRINT2_SMOKE_CHECKLIST.md`
- `5_AI/92_DEMO_SCRIPT.md`
- `5_AI/93_KNOWN_RISKS_AND_FIXES.md`

Resolution:

- Kept the more complete QA checklist/demo/risk content.
- Updated wording to target merged `sprint2/integration`, not standalone `feature/s2-qa-docs`.
- Added/kept `5_AI/94_SPRINT2_FINAL_HANDOFF_DRAFT.md` as a reusable runtime-smoke template.

No code merge conflicts occurred.

A post-merge integration issue was found in `AI_Study_Hub_v2/Program.cs`: duplicate `IEmbeddingService` and `IRagSearchService` registrations from an older coordinator wiring plus latest AI 2 merge. This was fixed in `80d7f2f` so each runtime service has one registration.

## Integrated Feature Scope

- Document ingestion/chunking/PDF extraction:
  - `AI_Study_Hub_v2/Services/Rag/ChunkingService.cs`
  - `AI_Study_Hub_v2/Services/Rag/DocumentIngestionService.cs`
  - `AI_Study_Hub_v2/Services/Rag/DocumentStorageReadService.cs`
  - `AI_Study_Hub_v2/Services/Rag/PdfTextExtractionService.cs`
- Fake deterministic embeddings and RAG search:
  - `AI_Study_Hub_v2/Services/Rag/FakeEmbeddingService.cs`
  - `AI_Study_Hub_v2/Services/Rag/RagSearchService.cs`
  - `AI_Study_Hub_v2/Controllers/RagController.cs`
- Grounded chat API and Groq client:
  - `AI_Study_Hub_v2/Controllers/AiChatController.cs`
  - `AI_Study_Hub_v2/Services/SemanticKernelRagChatService.cs`
  - `AI_Study_Hub_v2/Services/GroqChatCompletionClient.cs`
  - `AI_Study_Hub_v2/Services/IAiChatService.cs`
  - `AI_Study_Hub_v2/Services/IAiChatCompletionClient.cs`
  - `AI_Study_Hub_v2/Services/AiChatException.cs`
- Blazor chat UI:
  - `AI_Study_Hub_v2/Components/Pages/AiChat.razor`
  - `AI_Study_Hub_v2/Components/Pages/AiChat.razor.css`
  - `AI_Study_Hub_v2/Services/AiChatApiClient.cs`
  - `AI_Study_Hub_v2/Components/Layout/NavMenu.razor`
  - `AI_Study_Hub_v2/Components/Pages/DocumentDetail.razor`
- QA/tests/docs:
  - `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/RagContractTests.cs`
  - Worker tests under `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Controllers` and `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services`
  - `5_AI/91_SPRINT2_SMOKE_CHECKLIST.md`
  - `5_AI/92_DEMO_SCRIPT.md`
  - `5_AI/93_KNOWN_RISKS_AND_FIXES.md`
  - `5_AI/94_SPRINT2_FINAL_HANDOFF_DRAFT.md`

## DI And Config Verified

Final `AI_Study_Hub_v2/Program.cs` was verified to include:

- `ITextExtractionService -> PdfTextExtractionService`
- `IChunkingService -> ChunkingService`
- `IDocumentStorageReadService -> SupabaseDocumentStorageReadService`
- `IDocumentIngestionService -> DocumentIngestionService`
- `IEmbeddingService -> FakeEmbeddingService`
- `IRagSearchService -> RagSearchService`
- `IAiChatService -> SemanticKernelRagChatService`
- `IAiChatCompletionClient -> GroqChatCompletionClient` via `AddHttpClient`
- `AiChatApiClient` typed `HttpClient`
- `RagOptions` bound from config
- `GroqOptions` bound from config

Note: the user later described `IDocumentStorageReadService -> DocumentStorageReadService`, but the actual integrated class name is `SupabaseDocumentStorageReadService` in `DocumentStorageReadService.cs`.

Final config defaults verified in both `appsettings.json` and `appsettings.Development.json`:

```text
Rag:ChunkSizeChars = 1000
Rag:ChunkOverlapChars = 200
Rag:DefaultTopK = 5
Rag:MaxTopK = 10
Rag:EmbeddingDimensions = 384
Rag:MaxContextChars = 6000
Groq non-secret defaults only
```

No `Groq:ApiKey` was committed. Use user-secrets or environment only.

## Status Convention Locked

Current `DocumentStatus` values:

- `Uploading`
- `Ready`
- `Processing`
- `Failed`

No `Indexed` status was introduced.

Sprint 2 convention:

- `Ready` is RAG-usable.
- `Processing` is ingestion in progress.
- `Failed` is unusable / ingestion failure.

## Build And Test Evidence From Coordinator

Build after each latest merge:

```text
After feature/s2-ingestion: PASS, 0 warnings, 0 errors
After feature/s2-embedding-search: PASS, 0 warnings, 0 errors
After feature/s2-chat-api: PASS, 0 warnings, 0 errors
After feature/s2-chat-ui: PASS, 0 warnings, 0 errors
After feature/s2-qa-docs conflict resolution: PASS, 0 warnings, 0 errors
After DI dedupe fix: PASS, 0 warnings, 0 errors
```

Final test command:

```powershell
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
```

Final test result:

```text
Passed: 140
Skipped: 1
Failed: 0
Total: 141
Skipped test: ListAsync_FilterByQ_ILikeMatch_FileName
```

## Secret Scan / Safety

- Changed-file secret scan was run after integration and returned no secret matches.
- No Groq API key was committed.
- No push was performed.
- Backend was not started in the coordinator merge session, so there was no process to stop.

## Runtime Smoke Status

Runtime smoke has not been executed yet.

Remaining blockers:

- Local Supabase stack must be running and configured.
- `Groq:ApiKey` must be set through user-secrets or environment.
- Backend must be run on `http://localhost:5240` and stopped after smoke.
- A small text-based PDF is needed. Avoid scanned/image-only PDFs because OCR is out of scope.
- API/browser/DB smoke evidence still needs to be captured.

## Next Runtime Smoke Steps

Run from `D:\FPT\summer2026\SWP391_parallel\s2_integration`.

1. Inspect current worktree first because unrelated dirty files were observed:

```powershell
git status --short --branch
git branch --show-current
git log --oneline -10
```

2. Set Groq key locally only:

```powershell
dotnet user-secrets set "Groq:ApiKey" "<key>" --project AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
```

3. Start or verify Supabase local stack:

```powershell
.\setup.ps1
# or, if already configured:
.\setup.ps1 -SkipUp -SkipBuild
```

4. Confirm ports:

```powershell
Get-NetTCPConnection -LocalPort 5432,8000,5240 -ErrorAction SilentlyContinue |
    Format-Table -AutoSize LocalAddress,LocalPort,State,OwningProcess

docker compose -f infra\supabase\docker-compose.yml ps
```

5. Build/test before runtime:

```powershell
dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
```

6. Start backend:

```powershell
cd AI_Study_Hub_v2
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240
```

7. Browser smoke:

- Login.
- Upload a small text-based PDF at `/documents/upload` with subject `SWP391`, semester `SU26`.
- Confirm document appears in `/documents`.
- Open document detail and confirm signed download still works.
- Open `/ai/chat`.
- Ask: `What is AI Study Hub used for?`
- Ask: `Which semester is mentioned in the document?`
- Ask: `Does the document mention quantum physics?`
- Confirm first two answers cite the uploaded PDF and the third refuses/falls back.

8. DB smoke:

```sql
select d.id, d.file_name, d.status, count(c.id) as chunk_count
from public.documents d
left join public.document_chunks c on c.document_id = d.id
where d.file_name ilike '%swp391-rag-demo-su26%'
group by d.id, d.file_name, d.status
order by d.created_at desc
limit 5;

select c.document_id, c.chunk_index, c.page_number, vector_dims(c.embedding) as dims
from public.document_chunks c
join public.documents d on d.id = c.document_id
where d.file_name ilike '%swp391-rag-demo-su26%'
order by c.chunk_index
limit 10;
```

Expected: chunk count > 0 and `dims = 384`.

9. API smoke:

- `POST /api/rag/search`
- `POST /api/ai/chat/ask`
- Verify citations and owner-scoped results.

10. Cleanup:

- Delete uploaded test document.
- Confirm chunks are deleted by cascade or not returned by RAG.
- Stop backend with Ctrl+C to avoid file locks.

## Known Risks For Next Session

- Worktree may have unrelated modified admin/dashboard UI files. Do not overwrite or revert them.
- pgvector behavior still requires real Postgres smoke; unit tests cannot fully prove provider-specific vector SQL.
- Groq can fail due missing key, 401, 429, timeout, or free-tier availability. Use `/api/rag/search` as retrieval fallback evidence.
- OCR/scanned PDFs are unsupported.
- Owner-isolation runtime smoke is pending and should be tested with two users if time allows.
- If build fails with locked binaries, stop any running backend before rebuilding.

## Quick Handoff Summary

The integration merge is complete and build/tests passed at commit `80d7f2f`. The next session should not merge more worker branches unless new commits exist. It should focus on runtime smoke: configure secrets/Supabase, run backend, upload a text-based PDF, verify chunks and 384-dim embeddings, test RAG search/chat/UI, cleanup, then stop backend.
