# 11 — Session 2026-05-25 Sprint 1 D1+D2 Handoff

**Status:** Sprint 1 Day 1 = ✅ DONE (schema). Day 2 = 🟡 code-complete, **smoke chưa chạy live**. Ready cho session sau hoàn tất D2 closeout + tiếp D3.
**Author session này:** OpenCode (kr/claude-opus-4.7)
**Time window:** 2026-05-25 (cả ngày)
**Stack thực tế:** Supabase Local Phase 2 stack UP (12 containers healthy), backend NOT running.

---

## 0. Tại sao có file này

Bridge context cho session sau. Đọc file này + `02_Resume_Pack.md` + `07_Phase2_Document_RAG_Plan.md` (v-final) là đủ để tiếp Sprint 1 D2 closeout → D3 mà không phải tra lại file 08/09/10.

---

## 1. Việc đã làm trong session này

### 1.1 Bootstrap & docs

| # | Việc | Ghi chú |
|---|---|---|
| 1 | Set Groq API key vào `dotnet user-secrets` | key `Groq:ApiKey` (Q1 file 07) |
| 2 | Lock L1-L10 trong `07_Phase2_Document_RAG_Plan.md` Section 2 (v-final) | Section 2.1 thêm `subject_code` + `semester` (Sprint 1 SCRUM-12/15). Section 3.1 schema patch + changelog |
| 3 | Init monorepo `D:\FPT\summer2026\SWP391` | `.gitignore` v-final cover cả Vietnamese unicode filenames + Supabase secrets/data + legacy folders + VS template |
| 4 | Merge team's `origin/main` (clean-arch skeleton + LICENSE + README) | Conflict resolved: kept local `.gitignore` rules, appended team's VS template. Merge commit `abb1335` |

### 1.2 D1 — Phase 2 schema (DONE, applied to live DB)

**Packages added (`AI_Study_Hub_v2.csproj`):**
- `Pgvector` 0.3.1 — `Vector` type only (NOT `UseVector()` on data source builder; that API doesn't exist in 0.3.1)
- `Pgvector.EntityFrameworkCore` 0.2.0 — provides `npgsql.UseVector()` at EF Core level + `using Pgvector.EntityFrameworkCore;`
- `Pgvector.Npgsql` (transitive) — typeof not used directly; pgvector type registration is via `npgsql.UseVector()` only

**Entities added (`AI_Study_Hub_v2/Data/Entities/`):**
- `DocumentStatus.cs` — enum (Uploading=0, Ready=1, Processing=2, Failed=3) → maps to PG ENUM `public.document_status`
- `Folder.cs` — user-scoped grouping; unique `(user_id, name)`
- `Document.cs` — carries `subject_code` + `semester` (Sprint 1 filters); `status` = ENUM
- `DocumentChunk.cs` — `Embedding` is `Pgvector.Vector` (dim=384, locked via `EmbeddingDimension` const)

**Configurations added (`AI_Study_Hub_v2/Data/Configurations/`):**
- `FolderConfiguration.cs`, `DocumentConfiguration.cs`, `DocumentChunkConfiguration.cs`
- `DocumentConfiguration` has composite index `ix_documents_subject_semester` for filter perf

**`AppDbContext` changes:**
- **UNSEALED** (was `sealed`) — needed so test layer can subclass to skip Phase 2 entities
- `OnModelCreating` adds: `HasPostgresExtension("pgcrypto")`, `HasPostgresExtension("vector")`, `HasPostgresEnum<DocumentStatus>(...)`, `ApplyConfigurationsFromAssembly(...)`
- `UpdateTimestamps()` extended to cover Folder + Document modify states
- 3 new `DbSet`s: `Folders`, `Documents`, `DocumentChunks`

**`Program.cs` + `AppDbContextFactory` changes:**
- Build shared `NpgsqlDataSourceBuilder` → `MapEnum<DocumentStatus>(pgName: "public.document_status")` → `Build()`
- `AddDbContext` uses the data source + `npgsql.UseVector()` (NOT data source level)
- `using Pgvector.EntityFrameworkCore;` and `using Pgvector.Npgsql;` directives required
- Both files mirror each other (factory used for `dotnet ef` design-time)

**Migration `20260525143314_AddDocumentSchema`:**
- EF auto-generated tables, FKs, indexes
- **Hand-patched `Up()` adds:**
  - `migrationBuilder.Sql(...)` — IVFFlat cosine index `ix_document_chunks_embedding` lists=100 (plan L7)
  - `migrationBuilder.Sql(...)` — `ENABLE ROW LEVEL SECURITY` on `folders`, `documents`, `document_chunks` (defence-in-depth, plan Section 3.1)
- **Hand-patched `Down()`** drops the IVFFlat index before tables

**Live DB state after `dotnet ef database update`:**
```
Tables (public): __EFMigrationsHistory, document_chunks, documents, folders, roles, users (6 total)
ENUM document_status: 4 values (uploading, ready, processing, failed)
Indexes on document_chunks: PK, FK index, (doc_id, chunk_index) unique, ivfflat on embedding
RLS: enabled on folders + documents + document_chunks
```

### 1.3 D1 — Test fix

**Problem:** EF InMemory provider can't map `Pgvector.Vector` or PG ENUM types → 20/38 tests fail with `'Vector' property 'DocumentChunk.Embedding' could not be mapped`.

**Fix (`AI_Study_Hub_v2.Tests/Support/TestDb.cs`):**
- Created private nested class `TestAppDbContext : AppDbContext` (was sealed → unsealed in D1)
- Overrides `OnModelCreating` to call `modelBuilder.Ignore<DocumentChunk>(); Ignore<Document>(); Ignore<Folder>();`
- `TestDb.CreateInMemory(...)` returns `new TestAppDbContext(options)` instead of `new AppDbContext(options)`
- Auth tests don't touch documents → safe to ignore P2 entities at test time

**Result:** 38/38 tests pass, 0 warning, 0 error.

### 1.4 D2 — Backend upload pipeline (CODE-COMPLETE, smoke pending)

**Bucket created:** `documents` bucket via Supabase Storage REST (POST `/storage/v1/bucket`):
- private (`public: false`)
- file_size_limit: `52428800` (50 MB)
- allowed_mime_types: `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document` (.docx), `application/vnd.openxmlformats-officedocument.presentationml.presentation` (.pptx), `application/msword` (.doc), `application/vnd.ms-powerpoint` (.ppt)

**Files added:**

| File | Role |
|---|---|
| `Services/Supabase/ISupabaseStorageClient.cs` | Interface: `UploadAsync`, `CreateSignedUrlAsync`, `DeleteAsync` |
| `Services/Supabase/SupabaseStorageClient.cs` | HttpClient impl. POST `object/{bucket}/{path}` for upload (header `x-upsert` optional). POST `object/sign/{bucket}/{path}` body `{expiresIn:N}` for signed URL. Composes full URL `<supabase_url>/storage/v1/<signedURL>`. DELETE idempotent (404 → swallow) |
| `Services/Supabase/SupabaseStorageException.cs` | (defined inline at bottom of `SupabaseStorageClient.cs`) |
| `Dtos/DocumentDtos.cs` | `UploadDocumentRequest` (regex `^[A-Z]{3}[0-9]{3}$` for SubjectCode, `^(SP\|SU\|FA\|WI)[0-9]{2}$` for Semester), `DocumentDto` (with optional `DownloadUrl`), `DocumentListQuery` |
| `Services/IDocumentService.cs` | Interface: Upload/List/GetById/Delete |
| `Services/DocumentService.cs` | EF + Storage impl. Constants: `MaxFileSizeBytes = 50MB`, `SignedUrlTtlSeconds = 300`, `BucketName = "documents"`. Validates MIME against `AllowedMimeTypes` set, validates folder ownership when FolderId given, deterministic storage path `users/{user_id_n}/{yyyy}/{guid_n}-{slug}`, best-effort storage cleanup if DB insert fails after upload, `SanitizeFileName` strips path/special chars + caps at 80 chars |
| `Services/DocumentException.cs` | Mirrors `AuthException` (statusCode + code + message) |
| `Controllers/DocumentsController.cs` | `[Authorize(JwtBearer)]`. `POST /api/documents/upload` (multipart, `[RequestSizeLimit(50MB)]` + `[RequestFormLimits]`). `GET /api/documents` (filter via `DocumentListQuery`). `GET /api/documents/{id:guid}` (returns DTO + signed URL). `DELETE /api/documents/{id:guid}`. `GetSupabaseUserIdFromClaims()` reads `ClaimTypes.NameIdentifier` fallback `"sub"` |
| `Program.cs` (modified) | `AddHttpClient<ISupabaseStorageClient, SupabaseStorageClient>` with base URL `<supabase_url>/storage/v1/` + `apikey` + `Authorization: Bearer <serviceRoleKey>`. `AddScoped<IDocumentService, DocumentService>` |

**Build status:** 0 warning, 0 error. Tests: 38/38 pass.

**Smoke status:** App started once on port 5240 (PID 28216) for boot verify, then stopped clean before this handoff. Live E2E (login → upload PDF → list → get signed URL → download → delete) **NOT YET RUN**.

### 1.5 Git history (this session)

```
0e4340c feat(documents): D2 backend upload pipeline — code-complete, smoke pending
c2d36cb feat(documents): add Phase 2 schema (folders, documents, document_chunks)
abb1335 merge: integrate team skeleton (clean-arch placeholder) with Phase 1 Auth code
2b5f0d5 feat(auth): close Phase 1 Supabase Local migration + repo bootstrap
```
All pushed to `origin/main` on GitHub `keit2901/SU26SWP10-AI-Study-Hub-team-4`. Working tree CLEAN.

---

## 2. Trạng thái cuối session

### Docker (12 containers UP healthy, Phase 2 profile active)

```
Phase 1 (always-on):
  supabase-db, supabase-kong, supabase-auth, supabase-rest,
  supabase-meta, supabase-studio, supabase-analytics

Phase 2 (--profile phase2):
  supabase-storage, supabase-vector, supabase-imgproxy,
  realtime-dev.supabase-realtime

Extra (legacy from earlier compose):
  aistudyhub-db (separate container, not in active stack — can ignore or remove later)
```

⚠️ **Pooler (Supavisor) intentionally skipped** — port 5432 conflict with `supabase-db`. Plan locks Supavisor as out-of-scope for Phase 1/2 (resume pack 02 Section 2 item 18). Backend connects directly to `supabase-db:5432`.

### App
- `dotnet build` clean
- App **NOT running**, port 5240 free
- 38/38 tests pass

### DB
```
auth.users          = 1 (admin@aistudyhub.local)
public.users        = 1 (admin)
public.roles        = 2 (Admin, Student)
public.folders      = 0
public.documents    = 0
public.document_chunks = 0

Migrations applied:
  20260524090408_InitialSupabaseAuth
  20260525143314_AddDocumentSchema

Indexes on document_chunks:
  PK_document_chunks (PK)
  IX_document_chunks_document_id (FK index)
  IX_document_chunks_document_id_chunk_index (unique composite)
  ix_document_chunks_embedding (ivfflat cosine, lists=100)

RLS enabled: folders, documents, document_chunks
```

### Storage
```
bucket 'documents' OK:
  public: false
  file_size_limit: 52428800 bytes (50 MB)
  allowed_mime_types: 5 entries (PDF/DOCX/PPTX/DOC/PPT)
  current objects: 0
```

### Secrets (`dotnet user-secrets`)
- `ConnectionStrings:Postgres` ✅
- `Supabase:JwtSecret` / `AnonKey` / `ServiceRoleKey` / `Url` ✅
- `Seed:DefaultAdmin:Password` ✅
- `Groq:ApiKey` ✅ (set this session)

### Git
Working tree clean. All commits pushed. Branch: `main`.

---

## 3. Việc CHƯA LÀM (carry to next session)

### 3.1 D2 closeout — smoke E2E (PRIORITY 1, ~15 phút)

Chưa verify live pipeline. Plan:

```
1. Start app on port 5240
2. POST /api/auth/login (admin@aistudyhub.local) → grab access_token
3. POST /api/documents/upload (multipart: file=<small.pdf>, subject_code=SWP391, semester=SU26)
   → expect 201 Created + DocumentDto
4. GET /api/documents → expect array with 1 entry
5. GET /api/documents/{id} → expect DTO with DownloadUrl (signed, 5 min TTL)
6. Open DownloadUrl in browser → expect file downloads
7. DELETE /api/documents/{id} → expect 204
8. Verify: storage bucket count=0, public.documents count=0
```

If smoke passes, commit message: `test(documents): D2 smoke E2E pass — upload/list/get/download/delete verified live`. No new code expected unless smoke surfaces a bug.

**Known unknowns to watch during smoke:**
- `[FromForm]` binding for `UploadDocumentRequest` together with `IFormFile file` parameter — verify minimal-API/MVC binds both from same multipart body
- `CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto)` — verify `id` route param matches `[HttpGet("{id:guid}")]` (it does — `id` parameter name is consistent)
- `subject_code` casing: service uppercases (`ToUpperInvariant()`) before insert; client sends `SWP391` already → no issue, but document for D5 client validation
- Storage signed URL goes through Kong (`http://localhost:8000/storage/v1/...`) — verify it's reachable from browser, not just from backend

### 3.2 D3 — SCRUM-12/26 Blazor upload form (~half day)

Per `07_Phase2_Document_RAG_Plan.md` Section 7 Step 3:
- MudBlazor file picker (`MudFileUpload`) accepts PDF/DOCX/PPTX
- Client-side validation: size cap, MIME, regex on subject_code/semester
- Post to backend via `HttpClient` (typed client similar to `AuthApiClient`)
- Show progress + success/error toast
- Reuse `MainLayout` from Phase 1

### 3.3 D4-D7 (per plan)

- D4: SCRUM-25/27 list page + download flow
- D5: SCRUM-28 server-side validation hardening + xUnit/NUnit tests for `DocumentService` (currently zero coverage on D2 code)
- D6: SCRUM-14 chunking (PdfPig) + ONNX BGE embedding + background hosted service
- D7: SCRUM-15 search/filter + E2E smoke + sprint demo

### 3.4 Test coverage gap

D2 added ~600 lines of service+controller code with **zero new tests**. Plan: add in D5 alongside SCRUM-28:
- `DocumentService` unit tests (mock `ISupabaseStorageClient` + EF InMemory + `TestAppDbContext`):
  - upload happy path
  - upload size > 50MB → 413
  - upload bad MIME → 415
  - upload empty file → 400
  - upload with non-existent folder → 404
  - upload with foreign-user folder → 404 (not 403, to avoid id-leak)
  - DB insert failure triggers storage cleanup
  - list with all filter combos (subject, semester, folder, q ILike)
  - get returns signed URL
  - get foreign user → 404
  - delete idempotent on missing storage object
- `DocumentsController` tests with stub `IDocumentService` mirroring `AuthController` pattern

---

## 4. Notes / gotchas đã biết

### 4.1 Pgvector 0.3.1 API surface

`UseVector()` on `NpgsqlDataSourceBuilder` does NOT exist in 0.3.1. Wasted ~30 min trying. Correct usage:
- Data source level: only `MapEnum<T>(...)` for the PG enum
- DbContext level: `options.UseNpgsql(dataSource, npgsql => { npgsql.UseVector(); npgsql.MigrationsAssembly(...); })`
- Required usings: `using Pgvector.EntityFrameworkCore;` (for `UseVector` on `NpgsqlDbContextOptionsBuilder`)
- The `using Pgvector.Npgsql;` directive present in `Program.cs` and `AppDbContextFactory.cs` is harmless but **not strictly needed** in 0.3.1 — left in case future versions add data-source-level helpers

### 4.2 AppDbContext sealed → unsealed

`AppDbContext` was `public sealed class` in Phase 1. Unsealed in D1 to enable `TestAppDbContext` subclass. **Don't re-seal** unless a different test strategy is adopted.

### 4.3 Vietnamese filenames in `.gitignore`

Earlier `.gitignore` mojibake'd via `Set-Content -Encoding UTF8` (BOM + bytes scrambled). Fixed by rewriting via `Write` tool (UTF-8 no BOM, byte-perfect) AND using **wildcard rules** instead of literal Unicode strings — encoding-agnostic on Windows precomposed-vs-decomposed Unicode. Don't write VN literals back into `.gitignore` via PowerShell `Out-File`/`Set-Content`.

### 4.4 Pooler (Supavisor) port conflict

`docker compose --profile phase2 up -d` will print one warning per attempt about pooler trying to bind 5432. Cosmetic — pooler is not in any active service path. Plan locks this as known. Don't waste time fixing.

### 4.5 Git push stderr noise

`git push` writes progress info to stderr. PowerShell renders it as red `NativeCommandError`. **Push actually succeeds** — look for the `xxxx..yyyy main -> main` line at the end. Use `2>&1 | Select-Object -Last 3` to suppress noise.

### 4.6 Migration column case

`__EFMigrationsHistory` column is `MigrationId` (PascalCase, quoted), NOT `migration_id`. PowerShell single-line psql commands with embedded `\""..."\""` quoting don't always pass through cleanly — write SQL to a temp file and use `docker cp + psql -f /tmp/file.sql` instead.

---

## 5. Resume procedure (cho session sau)

### 5.1 Load context (~2 phút đọc)

Order:
1. **`02_Resume_Pack.md`** — primary state (sync, not yet updated for D1+D2; flag for next session)
2. **This file (`11_Session_2026-05-25_Sprint1_D1D2_Handoff.md`)** — bridge to today's work
3. **`07_Phase2_Document_RAG_Plan.md`** v-final — Section 2 lock + Section 7 step plan
4. **`08_Session_2026-05-24_Close_Phase1_Handoff.md`** — only if needing Phase 1 history
5. (skip 09/10/10b unless redoing demo prep)

### 5.2 Verify state

```powershell
# === Resume verify session 2026-05-26+ ===
$ok = $true

"--- 1. Containers (Phase 1 + Phase 2 = 11 expected) ---"
$expected = @('supabase-db','supabase-kong','supabase-auth','supabase-rest','supabase-meta','supabase-studio','supabase-analytics','supabase-storage','supabase-vector','realtime-dev.supabase-realtime','supabase-imgproxy')
foreach ($c in $expected) {
    $st = docker inspect $c --format '{{.State.Status}}' 2>$null
    if ($st -eq 'running') { "OK: $c" } else { "FAIL: $c=$st"; $ok = $false }
}

"--- 2. DB tables ---"
docker exec supabase-db psql -U postgres -d postgres -c "\dt public.*" 2>$null

"--- 3. Migrations ---"
$sql = "C:\Users\pc\AppData\Local\Temp\opencode\verify_mig.sql"
'SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY 1;' | Out-File -FilePath $sql -Encoding ascii -NoNewline
docker cp $sql supabase-db:/tmp/verify_mig.sql 2>&1 | Out-Null
docker exec supabase-db psql -U postgres -d postgres -At -f /tmp/verify_mig.sql 2>&1
# expect 2 lines: 20260524090408_InitialSupabaseAuth, 20260525143314_AddDocumentSchema

"--- 4. Storage bucket 'documents' ---"
$secrets = dotnet user-secrets list --project "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj" 2>&1
$svcKey = ($secrets | Select-String -Pattern '^Supabase:ServiceRoleKey\s*=\s*(.+)$').Matches[0].Groups[1].Value.Trim()
$h = @{ "Authorization" = "Bearer $svcKey"; "apikey" = $svcKey }
try {
    $r = Invoke-RestMethod -Uri "http://localhost:8000/storage/v1/bucket/documents" -Headers $h -Method GET
    "OK: bucket private=$(-not $r.public) size_limit=$($r.file_size_limit) mime_count=$($r.allowed_mime_types.Count)"
} catch { "FAIL: bucket fetch $($_.Exception.Message)"; $ok = $false }

"--- 5. Secrets present (5 keys) ---"
foreach ($k in 'Supabase:JwtSecret','Supabase:AnonKey','Supabase:ServiceRoleKey','Seed:DefaultAdmin:Password','ConnectionStrings:Postgres','Groq:ApiKey') {
    if ($secrets -match [regex]::Escape($k)) { "OK: $k" } else { "FAIL: $k missing"; $ok = $false }
}

"--- 6. Build + tests ---"
$build = dotnet build "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj" --nologo -v q 2>&1 | Select-Object -Last 3
if ($build -match 'Build succeeded' -or $build -match '0 Error') { "OK: build clean" } else { "FAIL: build"; $build; $ok = $false }
$test = dotnet test "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj" --nologo -v q 2>&1 | Select-Object -Last 2
if ($test -match 'Passed:    38') { "OK: 38/38 tests" } else { "FAIL: tests"; $test; $ok = $false }

"--- 7. Git ---"
$gs = git -C "D:\FPT\summer2026\SWP391" status --short
if (-not $gs) { "OK: working tree clean" } else { "WARN: uncommitted: $gs" }
git -C "D:\FPT\summer2026\SWP391" log --oneline -3

""
if ($ok) { "=== STATE OK — proceed to D2 smoke or D3 ===" } else { "=== STATE LECH — STOP and report ===" }
```

### 5.3 Default action plan

In order (override per Kiệt's call):

1. **D2 closeout smoke** (Section 3.1 above). Boot app → run 8-step E2E → commit verify message. Stop on any failure.
2. **D3 Blazor upload form** (Section 3.2). Per plan Section 7 Step 3.
3. Update `02_Resume_Pack.md` Section 9 schema + Section 12 backlog as D-days close. Don't let docs drift.

---

## 6. Quick Facts

```
Stack:         aistudyhub-supabase Phase 1 + Phase 2 (11 containers running)
DB:            postgres @ localhost:5432  (1 admin, 0 documents)
Kong gateway:  http://localhost:8000  (Studio + GoTrue + REST + Storage)
Storage:       http://localhost:8000/storage/v1/  (bucket 'documents' private, 50MB)
Backend:       http://localhost:5240  (NOT running)
Migrations:    InitialSupabaseAuth + AddDocumentSchema
Admin login:   admin@aistudyhub.local / <pwd ở user-secrets:Seed:DefaultAdmin:Password>
Tests:         38/38 pass; build 0/0 warn/err
Git:           clean, 0e4340c on origin/main; 4 commits this session
Sprint 1:      D1=DONE, D2=code-complete (smoke pending), D3-D7 not started
Phase 2 plan:  07 v-final, L1-L10 locked
Open Q:        none — Q1-Q10 all answered + locked
```

---

**END.** Session đóng sạch. App stopped, stack UP, DB clean, git pushed. Sẵn sàng cho session sau bắt đầu bằng D2 smoke E2E rồi tiếp D3.
