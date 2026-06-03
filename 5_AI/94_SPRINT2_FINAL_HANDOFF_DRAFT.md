# Sprint 2 RAG Final Handoff Draft

Use this draft after the integration branch has merged AI 1-5. Do not mark runtime smoke items as pass unless they were actually run.

## Context Loaded

- `D:\FPT\summer2026\SWP391\5_AI\00_START_HERE.md`
- `D:\FPT\summer2026\SWP391\5_AI\01_SHARED_CONTEXT.md`
- `D:\FPT\summer2026\SWP391\5_AI\02_CONTRACT_FIRST_PLAN.md`
- `D:\FPT\summer2026\SWP391\5_AI\90_INTEGRATION_MERGE_CHECKLIST.md`
- `D:\FPT\summer2026\SWP391\5_AI\91_SPRINT2_SMOKE_CHECKLIST.md`
- `D:\FPT\summer2026\SWP391\5_AI\93_KNOWN_RISKS_AND_FIXES.md`

## Integration State

- Worktree: `D:\FPT\summer2026\SWP391_parallel\s2_integration`
- Branch: `sprint2/integration`
- Contract base: `ccbaedd feat(rag): add sprint 2 shared contracts`
- Worker branches merged in order:
  - `feature/s2-ingestion`
  - `feature/s2-embedding-search`
  - `feature/s2-chat-api`
  - `feature/s2-chat-ui`
  - `feature/s2-qa-docs`

## Build And Test Evidence

Fill this with the final coordinator results:

```text
Build after AI 1 ingestion merge:
Build after AI 2 embedding/search merge:
Build after AI 3 chat API merge:
Build after AI 4 chat UI merge:
Build after AI 5 QA/docs merge:
Final test command:
Final test result:
```

## Runtime Smoke Evidence

Runtime smoke is not complete until the steps in `5_AI/91_SPRINT2_SMOKE_CHECKLIST.md` are executed.

```text
Runtime smoke: NOT RUN
Reason: pending local Supabase stack, Groq key, backend start, PDF upload, API/browser smoke, cleanup.
```

Fill after integrated runtime smoke:

```text
Branch/commit:
Supabase stack:
App URL:
Uploaded demo file:
Document id:
Document status transition:
Chunk count:
Embedding dimensions observed:
/api/rag/search result:
/api/ai/chat/ask result:
/ai/chat browser result:
Unrelated question behavior:
Owner isolation result:
Delete cleanup result:
Known deviations:
```

## Integrated Files Of Interest

- `AI_Study_Hub_v2/Program.cs`: RAG/chat DI, Groq HTTP client, UI API client registration.
- `AI_Study_Hub_v2/Services/Rag/*`: extraction, chunking, ingestion, embedding, search.
- `AI_Study_Hub_v2/Controllers/RagController.cs`: `POST /api/rag/search`.
- `AI_Study_Hub_v2/Controllers/AiChatController.cs`: `POST /api/ai/chat/ask`.
- `AI_Study_Hub_v2/Controllers/DocumentsController.cs`: upload endpoints plus manual re-ingest if integrated.
- `AI_Study_Hub_v2/Services/SemanticKernelRagChatService.cs`: grounded RAG prompt and source mapping.
- `AI_Study_Hub_v2/Components/Pages/AiChat.razor`: chat UI with citations.
- `AI_Study_Hub_v2/Components/Pages/DocumentDetail.razor`: document detail chat launch, if present.
- `5_AI/91_SPRINT2_SMOKE_CHECKLIST.md`: full smoke checklist.
- `5_AI/92_DEMO_SCRIPT.md`: demo talk track.
- `5_AI/93_KNOWN_RISKS_AND_FIXES.md`: risk/fix playbook.

## Decisions Locked Or Assumed

- `DocumentStatus.Ready` is the Sprint 2 RAG-usable status.
- No `Indexed` enum is introduced.
- Embedding dimension remains 384.
- Fake deterministic embeddings are acceptable for Sprint 2 demo unless a real approved embedding implementation replaces them.
- Groq defaults are non-secret in appsettings; `Groq:ApiKey` must be supplied by user-secrets or environment.
- Scanned PDFs/OCR are out of scope for Sprint 2.

## Remaining Runtime Blockers

- Local Supabase stack must be running and configured.
- `Groq:ApiKey` must be configured locally without committing it.
- Backend must be started on `http://localhost:5240` and stopped after smoke.
- Text-based PDF demo file is required.
- Browser/API/DB smoke evidence must be captured.

## Resume If Paused

Run from `D:\FPT\summer2026\SWP391_parallel\s2_integration`:

```powershell
git status --short
git branch --show-current
dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
```

Then execute `5_AI/91_SPRINT2_SMOKE_CHECKLIST.md` and fill the runtime smoke evidence section.
