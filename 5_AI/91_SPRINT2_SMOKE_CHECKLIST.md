# Sprint 2 RAG Smoke Checklist

Scope: QA/integration smoke guidance for `feature/s2-qa-docs` in `D:\FPT\summer2026\SWP391_parallel\s2_qa`. This file records only verified results from this worktree and leaves runtime smoke unchecked until it is actually run.

## Verified Session Evidence

- Required setup commands were run from `D:\FPT\summer2026\SWP391_parallel\s2_qa`.
- Branch: `feature/s2-qa-docs`.
- Worktree before edits: clean (`git status --short` produced no output).
- Recent commits observed: `9c243c1 docs(rag): add sprint 2 smoke guidance`, `ccbaedd feat(rag): add sprint 2 shared contracts`, `c35e991 docs(session): update Resume Pack after D6 commit`, `2a0c5d5 feat(documents): D6 folder picker and move-to-folder flow`, `e03423e docs(session): close 14 - D5 backend tests + Resume Pack refresh + F1 MIME drift fix`.
- Baseline build before QA edits: `dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo` passed with 0 warnings and 0 errors.
- Baseline tests before QA edits: `dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo --no-build` passed with 105 passed, 1 skipped, 0 failed, total 106.
- Runtime smoke was not executed in this session: `infra/supabase/.env` is absent in this QA worktree, ports `5432`, `8000`, and `5240` had 0 listeners, and `docker compose -f infra\supabase\docker-compose.yml ps` emitted missing environment variable warnings.
- User-secrets key names were checked without printing values: `Supabase:JwtSecret`, `Supabase:AnonKey`, `Supabase:ServiceRoleKey`, `Seed:DefaultAdmin:Password`, `ConnectionStrings:Postgres`, and `Groq:ApiKey` are present.

## Current Branch Status

| Area | Status | Evidence / Note |
|---|---|---|
| Shared DTOs | Ready | `Dtos/RagDtos.cs`, `Dtos/AiChatDtos.cs` compile. |
| RAG interfaces | Ready | `Services/Rag/RagContracts.cs` compiles. |
| RAG/Groq options | Ready | `Options/RagOptions.cs`, `Options/GroqOptions.cs` compile; non-secret config sections exist in appsettings. |
| Status enum | Ready for contract baseline | `DocumentStatus` has `Uploading`, `Ready`, `Processing`, `Failed`. |
| Build/unit tests | Passed before QA edits | See verified evidence above. |
| Supabase stack | Not verified | `.env` missing in this worktree; services not listening. |
| Ingestion pipeline | Not implemented on this branch | Contracts only; no upload-triggered chunking service registered. |
| `/api/rag/search` | Not implemented on this branch | No RAG controller merged. |
| `/api/ai/chat/ask` | Not implemented on this branch | No AI chat controller/service merged. |
| `/ai/chat` UI | Not implemented on this branch | No chat page/nav entry merged. |

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
- `8000` is listening for Supabase gateway, Storage, and Auth.
- `5240` is free until `dotnet run` starts.

## Baseline Verification

```powershell
dotnet build AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.sln --nologo --no-build
```

Expected:

- Build succeeds with no errors.
- Tests pass; record exact pass/skip/fail counts because Sprint 2 worker branches may change the total.

## Test Document

Use a tiny text-based PDF. Avoid scanned PDFs because OCR is out of Sprint 2 scope unless a worker branch explicitly adds it.

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

| Step | Status | Command / Action | Expected Result |
|---|---|---|---|
| 1. Start backend | [ ] Not run | `$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --no-launch-profile --urls http://localhost:5240` from `AI_Study_Hub_v2` | App starts on `http://localhost:5240`; no build lock errors. |
| 2. Login | [ ] Not run | Open `/login` or call `POST /api/auth/login` | Valid admin/student token returned; user can access documents. |
| 3. Upload | [ ] Not run | Open `/documents/upload`; upload test PDF with `SWP391` / `SU26` | Upload succeeds; document id returned; file appears in `/documents`. |
| 4. Download/detail | [ ] Not run | Open document detail | Signed download URL works; metadata is correct. |
| 5. Chunks | [ ] Not run | Run DB chunk query in the next section | Uploaded document has `chunk_count > 0`. |
| 6. Embeddings | [ ] Not run | Run DB embedding query in the next section | Each chunk has non-null embedding with `vector_dims(...) = 384`. |
| 7. RAG search | [ ] Not run | `POST /api/rag/search` with answerable query | HTTP 200; 1-5 owner-only results with source labels, file name, page/chunk, excerpt, score. |
| 8. Chat ask | [ ] Not run | `POST /api/ai/chat/ask` with answerable question | HTTP 200; grounded answer; `sources` non-empty; `refusalReason` null. |
| 9. UI citation | [ ] Not run | Open `/ai/chat`; ask same question | Browser shows answer and source citation cards/labels. |
| 10. Fallback | [ ] Not run | Ask `Does the document mention quantum physics?` | Refusal/fallback/no unsupported claim; no irrelevant citation as proof. |
| 11. Delete cleanup | [ ] Not run | Delete document via UI/API; run cleanup query | Chunks removed by cascade or no longer returned by RAG search. |
| 12. Stop backend | [ ] Not run | Ctrl+C in `dotnet run` terminal | Port `5240` released; future build/test not locked. |

## DB Verification Commands

Adapt the file-name fragment if the demo file differs.

```sql
select d.id, d.file_name, d.status, count(c.id) as chunk_count
from public.documents d
left join public.document_chunks c on c.document_id = d.id
where d.file_name ilike '%swp391-rag-demo-su26%'
group by d.id, d.file_name, d.status
order by max(d.created_at) desc
limit 5;
```

Expected after upload/index:

- Uploaded document is present.
- `status` matches the integrated convention (`Ready`/`Processing`/`Indexed` equivalent as documented by coordinator).
- `chunk_count > 0` once ingestion completes.

```sql
select c.document_id, c.chunk_index, c.page_number, vector_dims(c.embedding) as dims, left(c.content, 160) as excerpt
from public.document_chunks c
join public.documents d on d.id = c.document_id
where d.file_name ilike '%swp391-rag-demo-su26%'
order by c.chunk_index
limit 10;
```

Expected:

- `dims = 384` for every row.
- `chunk_index` follows the stored 0-based convention.
- `page_number` is 1-based when known and may be null when extraction cannot detect it.
- Excerpt contains text from the demo PDF.

Cleanup query after delete:

```sql
select count(*) as remaining_chunks
from public.document_chunks
where document_id = '<deleted-document-guid>';
```

Expected: `remaining_chunks = 0`, unless the integrated branch intentionally uses soft-delete semantics; in that case, verify RAG search no longer returns the deleted document.

## API Smoke Commands

Use a real access token from login.

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

## Browser Smoke Questions

- `What is AI Study Hub used for?`
- `Which semester is mentioned in the document?`
- `Does the document mention quantum physics?`

Expected browser behavior:

- First two questions answer from the uploaded PDF and show source citations.
- Third question refuses/falls back rather than inventing details.
- Loading/error states are visible and do not crash the page.

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
