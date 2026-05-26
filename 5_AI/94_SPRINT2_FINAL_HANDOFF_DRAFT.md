# Sprint 2 RAG Final Handoff Draft

Use this draft after the integration branch has merged AI 1-5. Do not mark runtime smoke items as pass unless they were actually run.

## Context Loaded

- `D:\FPT\summer2026\SWP391\5_AI\00_START_HERE.md`
- `D:\FPT\summer2026\SWP391\5_AI\01_SHARED_CONTEXT.md`
- `D:\FPT\summer2026\SWP391\5_AI\02_CONTRACT_FIRST_PLAN.md`
- `D:\FPT\summer2026\SWP391\5_AI\50_AI5_QA_INTEGRATION_DOCS.md`
- Existing QA docs inspected: `90_INTEGRATION_MERGE_CHECKLIST.md`, `91_SPRINT2_SMOKE_CHECKLIST.md`, `92_DEMO_SCRIPT.md`, `93_KNOWN_RISKS_AND_FIXES.md`
- Previous session rules inspected: `previous_session\rule.md`, `previous_session\02_Resume_Pack.md`

## Verified State At Start

- Worktree: `D:\FPT\summer2026\SWP391_parallel\s2_qa`
- Branch: `feature/s2-qa-docs`
- `git status --short`: clean before QA edits
- `git branch --show-current`: `feature/s2-qa-docs`
- `git log --oneline -5` at start:

```text
9c243c1 docs(rag): add sprint 2 smoke guidance
ccbaedd feat(rag): add sprint 2 shared contracts
c35e991 docs(session): update Resume Pack after D6 commit
2a0c5d5 feat(documents): D6 folder picker and move-to-folder flow
e03423e docs(session): close 14 - D5 backend tests + Resume Pack refresh + F1 MIME drift fix
```

## Build And Test Evidence

Baseline before QA edits:

```text
dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
Result: PASS, 0 Warning(s), 0 Error(s)
```

```text
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo --no-build
Result: PASS, 105 passed, 1 skipped, 0 failed, total 106
Skipped: ListAsync_FilterByQ_ILikeMatch_FileName
```

Final verification after QA edits:

```text
dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
Result: PASS, 0 Warning(s), 0 Error(s)

dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo --no-build
Result: PASS, 110 passed, 1 skipped, 0 failed, total 111
Skipped: ListAsync_FilterByQ_ILikeMatch_FileName
```

## Runtime Smoke Evidence

Status for this QA session:

```text
Runtime smoke: NOT RUN
Reason: infra/supabase/.env is absent in this QA worktree; ports 5432, 8000, and 5240 had 0 listeners; docker compose ps emitted missing environment variable warnings.
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

## Files Changed By AI 5

- `5_AI/91_SPRINT2_SMOKE_CHECKLIST.md`: detailed preflight, runtime checklist, DB/API/browser/security smoke, evidence template, and current not-run status.
- `5_AI/92_DEMO_SCRIPT.md`: 5-7 minute Vietnamese/simple-English talk track, sample demo questions, DB/API proof points, cleanup, and backup plans.
- `5_AI/93_KNOWN_RISKS_AND_FIXES.md`: integration blockers, conflict files, contract compatibility checks, and fix playbook for ingestion/search/chat/UI risks.
- `5_AI/94_SPRINT2_FINAL_HANDOFF_DRAFT.md`: this handoff template with verified baseline details and placeholders for integration smoke.
- `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/RagContractTests.cs`: low-conflict contract/default tests for DTOs, options, embedding dimension constant, and ingestion result shape.

## Decisions Locked Or Assumed

- No schema/migration changes were made by AI 5.
- No `Program.cs`, `.csproj`, or appsettings changes were made by AI 5.
- No ingestion, embedding/vector search, chat API, or Blazor chat UI feature implementation was added by AI 5.
- QA docs do not claim runtime pass because runtime smoke was not executed.
- Contract branch currently has shared RAG DTOs/interfaces/options but not the feature implementations.

## Integration Blockers Found

- AI 1 ingestion is still required for text extraction, chunking, embedding persistence trigger, and upload/index status semantics.
- AI 2 search is still required for `POST /api/rag/search`, top-K retrieval, pgvector SQL, and owner filtering.
- AI 3 chat API is still required for `POST /api/ai/chat/ask`, grounded prompt, source response, no-source fallback, and Groq error mapping.
- AI 4 chat UI is still required for `/ai/chat`, answer rendering, citations, loading/error/fallback states.
- QA runtime smoke needs local Supabase `.env`/containers before pass/fail can be recorded.

## Recommended Next Merge/Fix

1. Merge AI 1 ingestion; run build/test and verify upload creates `document_chunks`.
2. Merge AI 2 search; run build/test and verify `/api/rag/search` owner-only results.
3. Merge AI 3 chat API; verify answerable question, no-source fallback, and Groq failure mapping.
4. Merge AI 4 UI; verify `/ai/chat` answer + citations + error states.
5. Merge AI 5 QA/docs/tests last; run full smoke checklist and fill runtime evidence.

## Known Limitations

- Runtime E2E was not run in the QA worktree due to missing local Supabase env/containers.
- Contract tests only protect DTO/options/default shapes; they do not validate real pgvector SQL or Groq calls.
- Scanned PDFs/OCR remain out of scope unless another branch explicitly implements OCR.
- Demo success depends on a text-based PDF, valid local Supabase stack, and Groq availability or retrieval fallback.

## Resume If Paused

Run from `D:\FPT\summer2026\SWP391_parallel\s2_qa`:

```powershell
git status --short
dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo --no-build
```

Then, after worker branches are merged into the integration branch, execute `5_AI/91_SPRINT2_SMOKE_CHECKLIST.md` and fill the runtime smoke evidence section in this draft.
