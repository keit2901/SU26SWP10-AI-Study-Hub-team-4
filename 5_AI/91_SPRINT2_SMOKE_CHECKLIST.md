# Sprint 2 RAG Smoke Checklist

Scope: QA/integration smoke guidance for `sprint2/integration` in `D:\FPT\summer2026\SWP391_parallel\s2_integration`. This file records verified build/test evidence and leaves runtime smoke unchecked until it is actually run.

## Verified Session Evidence

- Required integration commands were run from `D:\FPT\summer2026\SWP391_parallel\s2_integration`.
- Branch: `sprint2/integration`.
- Contract base: `ccbaedd feat(rag): add sprint 2 shared contracts`.
- Latest worker heads merged: `3fff2eb`, `91998ac`, `d742aa8`, `b08004c`, `80f5bb9`.
- Build passed after each implementation branch merge.
- Runtime smoke was not executed in this merge session; it still requires local Supabase services and a `Groq:ApiKey` configured through user-secrets or environment.
- Do not print secret values in smoke evidence.

## Current Integration Status

| Area | Status | Evidence / Note |
|---|---|---|
| Shared DTOs | Ready | `Dtos/RagDtos.cs`, `Dtos/AiChatDtos.cs` compile. |
| RAG interfaces | Ready | `Services/Rag/RagContracts.cs` compiles. |
| RAG/Groq options | Ready | `Options/RagOptions.cs`, `Options/GroqOptions.cs` compile; appsettings contain non-secret defaults only. |
| Status enum | Ready | `DocumentStatus` has `Uploading`, `Ready`, `Processing`, `Failed`; `Ready` is the RAG-usable status. |
| Ingestion pipeline | Integrated | PDF upload triggers ingestion; manual `POST /api/documents/{id}/ingest` is available for smoke/debug. |
| `/api/rag/search` | Integrated | RAG controller returns owner-scoped top-K chunks and citations. |
| `/api/ai/chat/ask` | Integrated | AI chat controller returns grounded answer/citations or controlled fallback/error. |
| `/ai/chat` UI | Integrated | Chat page, nav link, and document-detail launch link are present. |
| Supabase stack | Not verified | Runtime smoke still pending. |

## Pre-Smoke Setup

Run from `D:\FPT\summer2026\SWP391_parallel\s2_integration` unless noted.

```powershell
git status --short --branch
git log --oneline -5
dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
```

If the local Supabase stack is not already configured in this worktree, initialize it before runtime smoke:

```powershell
.\setup.ps1
```

If `.env` already exists and the stack is up from another session, refresh app user-secrets without regenerating values:

```powershell
.\setup.ps1 -SkipUp -SkipBuild
```

Set the Groq key only through user-secrets or environment, never appsettings:

```powershell
dotnet user-secrets set "Groq:ApiKey" "<key>" --project AI_Study_Hub_v2\AI_Study_Hub_v2.csproj
```

Confirm ports and containers:

```powershell
Get-NetTCPConnection -LocalPort 5432,8000,5240 -ErrorAction SilentlyContinue |
    Format-Table -AutoSize LocalAddress,LocalPort,State,OwningProcess

docker compose -f infra\supabase\docker-compose.yml ps
```

Expected before app start:

- `5432` is listening for Supabase Postgres.
- `8000` is listening for Supabase gateway, Storage, and Auth.
- `5240` is free until `dotnet run` starts.

## Test Document

Use a tiny text-based PDF. Avoid scanned PDFs because OCR is out of Sprint 2 scope.

Suggested content:

```text
AI Study Hub is a platform for SWP391 students.
The system uses Retrieval-Augmented Generation to answer questions from uploaded documents.
The demo semester is SU26.
Students can verify answers by checking source citations from their uploaded files.
```

Recommended metadata:

- File name: `swp391-rag-demo-su26.pdf`
- Subject: `SWP391`
- Semester: `SU26`
- Folder: optional, for example `Sprint 2 Demo`

## End-To-End Smoke Checklist

Do not check an item until it has been run and verified on the integrated branch.

1. Start backend:

```powershell
cd AI_Study_Hub_v2
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240
```

2. Open `http://localhost:5240/login`.
3. Login with seeded admin or a registered student account.
4. Upload the test PDF from `http://localhost:5240/documents/upload`.
5. Confirm upload succeeds and the document appears in `http://localhost:5240/documents`.
6. Open document detail and confirm signed download still works.
7. Confirm document reaches `Ready` after ingestion or inspect the failure message if extraction failed.
8. Confirm chunks exist:

```sql
select d.id, d.file_name, d.status, count(c.id) as chunk_count
from public.documents d
left join public.document_chunks c on c.document_id = d.id
where d.file_name ilike '%swp391-rag-demo-su26%'
group by d.id, d.file_name, d.status
order by d.created_at desc
limit 5;
```

9. Confirm embeddings are 384-dimensional:

```sql
select c.document_id, c.chunk_index, c.page_number, vector_dims(c.embedding) as dims
from public.document_chunks c
join public.documents d on d.id = c.document_id
where d.file_name ilike '%swp391-rag-demo-su26%'
order by c.chunk_index
limit 10;
```

Expected: `dims = 384` for every returned row.

10. Call `POST /api/rag/search` with a valid access token:

```powershell
$token = "<paste access_token>"
$searchBody = @{
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
    -Body $searchBody
```

Expected `/api/rag/search` response:

- HTTP 200.
- 1-5 results.
- Each result includes `sourceLabel`, `documentId`, `fileName`, `chunkIndex`, `pageNumber`, `contentExcerpt`, and `score`.
- Results belong only to the authenticated user.

11. Call `POST /api/ai/chat/ask`:

```powershell
$askBody = @{
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
    -Body $askBody
```

Expected `/api/ai/chat/ask` response:

- HTTP 200.
- `answer` mentions `SU26` for the sample question.
- `sources` contains at least one citation with file name and excerpt.
- `refusalReason` is null for answerable questions.

12. Open `http://localhost:5240/ai/chat`.
13. Ask browser questions:

- `What is AI Study Hub used for?`
- `Which semester is mentioned in the document?`
- `Does the document mention quantum physics?`

Expected browser behavior:

- First two questions answer from the uploaded PDF and show source citations.
- Third question refuses/falls back rather than inventing details.
- Loading/error states are visible and do not crash the page.

14. Delete the uploaded document from UI or API.
15. Confirm chunks are deleted by cascade or no longer returned:

```sql
select count(*) as remaining_chunks
from public.document_chunks
where document_id = '<deleted-document-guid>';
```

16. Stop the backend with Ctrl+C to avoid file locks.

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

Copy this into the final integration handoff after the merged smoke run. Leave fields as `not run` if they were not executed.

```text
Branch/commit:
Supabase stack:
App URL:
Build command/result:
Test command/result:
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
