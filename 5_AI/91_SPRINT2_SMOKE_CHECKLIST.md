# Sprint 2 RAG Smoke Checklist

Scope: QA/integration smoke guidance for `sprint2/integration` in `D:\FPT\summer2026\SWP391_parallel\s2_integration`. This file is intentionally local to the integration worktree and does not edit the original planning folder.

## Session Evidence

- Context loaded from original planning files: `00_START_HERE.md`, `01_SHARED_CONTEXT.md`, `02_CONTRACT_FIRST_PLAN.md`, `50_AI5_QA_INTEGRATION_DOCS.md`.
- Start branch/status: `feature/s2-qa-docs`, clean worktree before docs were created.
- Base commit: `ccbaedd feat(rag): add sprint 2 shared contracts`.
- Baseline build: `dotnet build AI_Study_Hub_v2.sln --nologo` passed with 0 warnings and 0 errors.
- Baseline tests: `dotnet test AI_Study_Hub_v2.sln --nologo` passed: 105 passed, 1 skipped, 0 failed, total 106.
- Runtime smoke not executed in this worktree because `infra/supabase/.env` is not present and `docker compose -f infra\supabase\docker-compose.yml ps` only emitted missing environment variable warnings.
- Port check found no listeners on `5432`, `8000`, or `5240` at smoke time.
- User-secrets key names exist for Supabase, Postgres, default admin password, and Groq API key; values were not printed.

## Current Branch Status

| Area | Status | Evidence / Note |
|---|---|---|
| Contract DTOs | Ready | `Dtos/RagDtos.cs`, `Dtos/AiChatDtos.cs` compile. |
| RAG interfaces | Ready | `Services/Rag/RagContracts.cs` compiles. |
| RAG options | Ready | `Options/RagOptions.cs`, `Options/GroqOptions.cs` compile and config sections exist. |
| Build | Passed | `dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo`: 0 warnings, 0 errors after final integration wiring. |
| Unit tests | Passed | `dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo --no-build`: 132 passed, 1 skipped. |
| Supabase stack | Not executed | Runtime smoke still needs local Supabase `.env`/containers and a user-secrets Groq key. |
| Ingestion | Integrated | PDF upload invokes ingestion; manual `POST /api/documents/{id}/ingest` is available for re-ingest/debug. |
| `/api/rag/search` | Integrated | `RagController` returns owner-scoped top-K chunk results. |
| `/api/ai/chat/ask` | Integrated | `AiChatController` returns grounded answers and citations via Groq when `Groq:ApiKey` is configured. |
| `/ai/chat` UI | Integrated | AI chat page and nav entry are available. |

## Pre-Smoke Setup

Run from `D:\FPT\summer2026\SWP391_parallel\s2_qa` unless noted.

```powershell
git status --short --branch
git log --oneline -5
```

If the local Supabase stack is not already configured in this worktree, initialize it before runtime smoke:

```powershell
.\setup.ps1
```

If `.env` already exists and the stack is up from another session, refresh app user-secrets without regenerating values:

```powershell
.\setup.ps1 -SkipUp -SkipBuild
```

Confirm ports and containers:

```powershell
Get-NetTCPConnection -LocalPort 5432,8000,5240 -ErrorAction SilentlyContinue |
    Format-Table -AutoSize LocalAddress,LocalPort,State,OwningProcess

docker compose -f infra\supabase\docker-compose.yml ps
```

Expected before app start:

- `5432` is listening for Supabase Postgres.
- `8000` is listening for Supabase gateway / Storage / Auth.
- `5240` is free until `dotnet run` starts.

## Baseline Verification

```powershell
cd AI_Study_Hub_v2
dotnet build AI_Study_Hub_v2.sln --nologo
dotnet test AI_Study_Hub_v2.sln --nologo
```

Expected on the contract baseline:

- Build succeeds with no errors.
- Tests pass with current count near `105 passed, 1 skipped`; if other Sprint 2 branches have merged, record the new count exactly.

## Sprint 2 Runtime Smoke

Use a tiny text-based PDF. Avoid scanned PDFs for the demo because OCR is not Sprint 2 scope unless explicitly merged.

1. Start app from `AI_Study_Hub_v2`:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240
```

2. Open `http://localhost:5240/login`.
3. Login with seeded admin or a registered student account.
4. Upload a PDF from `http://localhost:5240/documents/upload` with:
   - Subject: `SWP391`
   - Semester: `SU26`
   - Optional folder: any owned folder or loose document
5. Confirm the document appears in `http://localhost:5240/documents`.
6. Confirm document detail loads and signed download works.
7. Confirm ingestion status reaches the integrated branch's agreed success state.
   - Contract baseline uses `DocumentStatus.Ready` for stored documents.
   - If ingestion branch adds `Processing` / `Indexed`, record the exact status transition here.
8. Confirm chunks exist for the uploaded document:

```sql
select d.id, d.file_name, d.status, count(c.id) as chunk_count
from public.documents d
left join public.document_chunks c on c.document_id = d.id
where d.file_name ilike '%<demo-file-name-fragment>%'
group by d.id, d.file_name, d.status
order by d.created_at desc
limit 5;
```

9. Confirm embeddings are populated and dimension remains 384:

```sql
select c.document_id, c.chunk_index, c.page_number, vector_dims(c.embedding) as dims
from public.document_chunks c
join public.documents d on d.id = c.document_id
where d.file_name ilike '%<demo-file-name-fragment>%'
order by c.chunk_index
limit 10;
```

Expected: `dims = 384` for every returned row.

10. If implemented, call `POST /api/rag/search`:

```powershell
$token = "<paste access_token>"
$body = @{
    query = "What is AI Study Hub used for?"
    subjectCode = "SWP391"
    semester = "SU26"
    topK = 5
} | ConvertTo-Json

Invoke-RestMethod `
    -Method Post `
    -Uri "http://localhost:5240/api/rag/search" `
    -Headers @{ Authorization = "Bearer $token" } `
    -ContentType "application/json" `
    -Body $body
```

Expected:

- HTTP 200.
- 1-5 results.
- Each result includes `sourceLabel`, `documentId`, `fileName`, `chunkIndex`, `pageNumber`, `contentExcerpt`, and `score`.
- Results belong only to the authenticated user.

11. If implemented, call `POST /api/ai/chat/ask`:

```powershell
$body = @{
    question = "Which semester is mentioned in the document?"
    subjectCode = "SWP391"
    semester = "SU26"
    topK = 5
} | ConvertTo-Json

Invoke-RestMethod `
    -Method Post `
    -Uri "http://localhost:5240/api/ai/chat/ask" `
    -Headers @{ Authorization = "Bearer $token" } `
    -ContentType "application/json" `
    -Body $body
```

Expected:

- HTTP 200.
- `answer` is grounded in uploaded content.
- `sources` contains at least one citation with file name and excerpt.
- `refusalReason` is null for answerable questions.

12. Open `http://localhost:5240/ai/chat` if the UI branch is merged.
13. Ask the same answerable question in the browser and confirm answer + citations render.
14. Ask an unrelated question such as `Does the document mention quantum physics?`
15. Expected unrelated-question behavior:
    - API/UI should refuse or fallback clearly.
    - It should not invent content or cite irrelevant chunks as proof.
16. Delete the uploaded document from the UI or API.
17. Confirm chunks are deleted by cascade or no longer returned:

```sql
select count(*) as remaining_chunks
from public.document_chunks
where document_id = '<deleted-document-guid>';
```

Expected: `remaining_chunks = 0`, or retrieval excludes the deleted document if soft-delete semantics are introduced later.
18. Stop the backend with Ctrl+C to avoid file locks.

## Security Smoke

Run after at least two users exist and each has one uploaded/indexed document.

| Step | Expected Result |
|---|---|
| Student A searches by Student B `documentId` | 404, empty result, or equivalent no-leak response. |
| Student A searches by shared `subjectCode`/`semester` | Only Student A chunks returned. |
| Unauthenticated call to RAG/search/chat endpoints | 401. |
| Invalid `topK` greater than `Rag:MaxTopK` | Clamped or rejected consistently. |
| Missing/blank query or question | 400 validation error. |

## Evidence Template

Copy this into the final integration handoff after the merged smoke run:

```text
Branch/commit:
Supabase stack:
App URL:
Build command/result:
Test command/result:
Uploaded demo file:
Document id:
Chunk count:
Embedding dimensions observed:
/api/rag/search result:
/api/ai/chat/ask result:
/ai/chat browser result:
Unrelated question behavior:
Delete cleanup result:
Known deviations:
```
