> **⚠️ SUPERSEDED 2026-05-26T03:41Z** — file này được tạo lúc 2026-05-26T01:25Z (commit `00d0268`) nhưng bị **superseded** bởi `11_Session_2026-05-25_Sprint1_D1D2_Handoff.md` (commit `fde0c2b`, 2026-05-26T01:31Z) — phiên bản canonical đầy đủ hơn.
> Giữ file này để bảo toàn history (rule §7 cấm xoá session log). Đừng đọc file này khi resume; đọc file `11_*_D1D2_Handoff.md` thay thế.

---

# 11 — Session 2026-05-25 Sprint 1 D1+D2 Handoff

**Status:** Sprint 1 day 1 done + day 2 backend code-complete (commit pushed `0e4340c`). Live smoke chưa chạy. Frontend Blazor chưa đụng.
**Author session B:** OpenCode (kr/claude-opus-4.7)
**Time:** 2026-05-25 (afternoon → evening, ~3h elapsed)
**Stack thực tế:** Supabase Local profile=phase2 UP healthy (12 containers tracked, 1 expected DOWN — pooler port conflict 5432, đã skip per Phase 1 decision item 18). Bucket `documents` created (private, 50MB, MIME whitelist 5 types).

---

## 0. Tại sao có file này

Bridge context cho session C. Đọc 3 files là đủ context:
1. **`02_Resume_Pack.md`** — primary state (Phase 1 + DB schema)
2. **`08_Session_2026-05-24_Close_Phase1_Handoff.md`** — close Phase 1
3. **`11_Session_2026-05-25_Sprint1_D1_D2_Handoff.md`** (file này) — Sprint 1 D1-D2 bridge

Kèm `07_Phase2_Document_RAG_Plan.md` v-final khi cần spec lại Phase 2 (đã LOCKED L1-L10).

---

## 1. Quyết định ngữ cảnh trong session B

| # | Quyết định | Note |
|---|---|---|
| B1 | **Sprint 1 giữ nguyên 11 items, cày 7 ngày.** Kiệt confirm. | 3/11 done (auth track), 8 còn (document track). Deadline 1 Jun. |
| B2 | **Q1-Q10 file 07 → L1-L10 = recommend toàn bộ.** | Kiệt confirm "GO với recommend". |
| B3 | **Schema: thêm `subject_code` + `semester` flat vào `public.documents`** (option A). | Không tạo bảng `subjects` riêng. Phase 3 normalize nếu cần. |
| B4 | **SCRUM-13 storage backend = Supabase Storage** (override Jira "Azure Blob"). | Kiệt đã sửa Jira description. |
| B5 | **Init monorepo ở `D:\FPT\summer2026\SWP391\`** + remote `https://github.com/keit2901/SU26SWP10-AI-Study-Hub-team-4`. | Merge với team's clean-arch skeleton (`AI_Study_HUB/`) qua `--allow-unrelated-histories`. |
| B6 | **AppDbContext bỏ `sealed`** để test layer subclass được. | `TestAppDbContext` ignore `Folder/Document/DocumentChunk` cho InMemory provider. |
| B7 | **Pgvector `UseVector()` chỉ dùng ở EF Core level, không ở data source level** (trong v0.3.1). | API khác doc cũ. ENUM `document_status` map qua `NpgsqlDataSourceBuilder.MapEnum`. |
| B8 | **Pooler/Supavisor disable** vì port 5432 conflict với supabase-db. | Confirm consistent với Phase 1 item 18 trong `02_Resume_Pack.md`. |

---

## 2. Việc đã hoàn tất (D1 + D2 backend)

### 2.1 D1 — Schema + entities + migration (commit `c2d36cb`)

**Packages thêm:**
- `Pgvector` 0.3.1
- `Pgvector.EntityFrameworkCore` 0.2.0

**3 entities mới:** `Folder`, `Document`, `DocumentChunk` + enum `DocumentStatus`
- Path: `AI_Study_Hub_v2/Data/Entities/{Folder,Document,DocumentChunk,DocumentStatus}.cs`
- Configurations: `AI_Study_Hub_v2/Data/Configurations/{Folder,Document,DocumentChunk}Configuration.cs`

**Migration:** `20260525143314_AddDocumentSchema.cs`
- 3 tables (folders, documents, document_chunks) + FK CASCADE đúng
- ENUM `public.document_status` (uploading/ready/processing/failed)
- Composite index `ix_documents_subject_semester(subject_code, semester)` cho filter perf
- Raw SQL: `IVFFlat lists=100` cosine index trên `document_chunks.embedding`
- Raw SQL: `ENABLE ROW LEVEL SECURITY` cả 3 bảng (defence in depth)

**Verify trên live DB (đã chạy 2026-05-25 ~14:35 UTC):**
```
public schema:  __EFMigrationsHistory, roles, users, folders, documents, document_chunks
ENUM:           document_status (4 values)
ivfflat index:  ix_document_chunks_embedding (lists=100, vector_cosine_ops)
RLS:            t/t/t cho folders/documents/document_chunks
```

**DI changes:**
- `AppDbContext.cs`: bỏ `sealed`, register `HasPostgresExtension(pgcrypto, vector)` + `HasPostgresEnum<DocumentStatus>`
- `Program.cs` + `AppDbContextFactory.cs`: dùng `NpgsqlDataSourceBuilder.MapEnum<DocumentStatus>` (không còn `UseVector` ở data source level)
- `TestDb.cs`: `TestAppDbContext` private subclass ignore Phase 2 entities cho InMemory provider

**Build/test status sau D1:** 0 warning 0 error, 38/38 pass.

### 2.2 D2 — Backend upload pipeline (commit `0e4340c`)

**Bucket setup (live, đã chạy qua Storage REST):**
- Name: `documents`
- Private (`public=false`)
- Max file size: 50 MB
- MIME whitelist: `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document` (.docx), `application/vnd.openxmlformats-officedocument.presentationml.presentation` (.pptx), `application/msword` (.doc), `application/vnd.ms-powerpoint` (.ppt)

**Files mới:**

| File | Vai trò |
|---|---|
| `Services/Supabase/ISupabaseStorageClient.cs` | interface |
| `Services/Supabase/SupabaseStorageClient.cs` | HttpClient impl (UploadAsync, CreateSignedUrlAsync, DeleteAsync) |
| `Services/IDocumentService.cs` | interface |
| `Services/DocumentService.cs` | EF + Storage implementation, owner-scoped |
| `Services/DocumentException.cs` | parallel với AuthException |
| `Dtos/DocumentDtos.cs` | UploadDocumentRequest + DocumentDto + DocumentListQuery |
| `Controllers/DocumentsController.cs` | POST /upload (multipart), GET /, GET /{id}, DELETE /{id} |
| `Program.cs` | DI: `AddHttpClient<ISupabaseStorageClient, SupabaseStorageClient>` + `AddScoped<IDocumentService>` |

**Endpoints:**
| Method | Path | Auth | Body | 2xx |
|---|---|---|---|---|
| POST | `/api/documents/upload` | Bearer | multipart: `file` + `subjectCode` + `semester` + `folderId?` | 201 + `DocumentDto` |
| GET | `/api/documents` | Bearer | query `?subjectCode=&semester=&folderId=&q=` | 200 + `IReadOnlyList<DocumentDto>` |
| GET | `/api/documents/{id}` | Bearer | — | 200 + `DocumentDto` (kèm `downloadUrl` 5-min signed) |
| DELETE | `/api/documents/{id}` | Bearer | — | 204 |

**Validation rules baked in DocumentService:**
- Subject regex `^[A-Z]{3}[0-9]{3}$` (vd `SWP391`) — tự upper
- Semester regex `^(SP|SU|FA|WI)[0-9]{2}$` (vd `SU26`) — tự upper
- Folder ownership check khi `FolderId` provided
- File size > 0 + ≤ 50 MB
- MIME ∈ whitelist 5 types
- Best-effort storage cleanup nếu DB insert fail sau upload thành công
- Best-effort storage delete nếu row delete OK nhưng object delete fail

**Status sau D2:** Build 0 warning 0 error. Tests 38/38 pass. **Live smoke CHƯA chạy.**

### 2.3 Repo bootstrap (commits `2b5f0d5` + `abb1335`)

- Init `git` ở `D:\FPT\summer2026\SWP391\` (monorepo root)
- `.gitignore` cover: secrets, DB volumes, build artifacts, legacy `AI_Study_Hub_Admin/`, scratch notes (`Skills/`, `Suggest_from_Claude/`), backups, Docker installer, VN unicode filenames qua wildcard
- Identity: `Kiệt <kakalga000@gmail.com>` (local repo only)
- Merged team's clean-arch skeleton (`AI_Study_HUB/` placeholder Domain/Application/Infrastructure/WebApi/BlazorWasm) qua `--allow-unrelated-histories`. **Skeleton không build, là scaffold reference. Code thật ở `AI_Study_Hub_v2/`.**
- Push 4 commits lên `keit2901/SU26SWP10-AI-Study-Hub-team-4` main: `2b5f0d5 → abb1335 → c2d36cb → 0e4340c`

### 2.4 Secrets

`dotnet user-secrets` (project `f7443cc6-...`):
```
ConnectionStrings:Postgres   = (Phase 1 unchanged)
Supabase:JwtSecret           = (Phase 1 unchanged)
Supabase:AnonKey             = (Phase 1 unchanged)
Supabase:ServiceRoleKey      = (Phase 1 unchanged)
Seed:DefaultAdmin:Password   = (Phase 1 unchanged)
Groq:ApiKey                  = gsk_HKsDan...XFR  ← NEW 2026-05-25 (B1 lock)
```

> **K-list new:** K6 — admin pwd raw value `LNF2DtxKWuYpiGv3SkHU` đã bị redact khỏi `previous_session/08:43` trước khi commit Git. Còn live trong user-secrets + password manager. Không cần rotate (chưa từng push).

### 2.5 File 07 status

`07_Phase2_Document_RAG_Plan.md` v1 → **v-final LOCKED**.
- Header: `Status: v-final LOCKED — Kiệt confirm Q1-Q10 = recommend toàn bộ vào 2026-05-25. GO BUILD.`
- Section 2 đã fill L1-L10 lock
- Section 2.1 NEW: thêm 2 cột `subject_code` + `semester` cho Sprint 1 SCRUM-12/15
- Section 3.1 schema `documents`: append 2 cột mới + index `ix_documents_subject_semester`
- Section 12 changelog updated

---

## 3. Việc CHƯA LÀM (chuyển session sau)

### 3.1 D2 closeout (~30 phút)

**Live smoke test pipeline upload — 5 bước:**
1. Login admin → lấy access token
2. Tạo PDF nhỏ test (vd qua `New-Item` + `[System.IO.File]::WriteAllBytes` hoặc copy 1 PDF có sẵn)
3. POST `/api/documents/upload` multipart kèm `file=@test.pdf&subjectCode=SWP391&semester=SU26`
4. GET `/api/documents` verify list trả về 1 row
5. GET `/api/documents/{id}` verify `downloadUrl` signed URL (5 min TTL) — fetch URL bằng curl, expect 200 + bytes
6. DELETE `/api/documents/{id}` → 204; verify GET /{id} = 404

**Acceptance:** tất cả 6 bước OK → mark D2 done.

### 3.2 D3-D7 (theo plan Sprint 1 7-day execution)

| Ngày | Items | Effort |
|---|---|---|
| D3 (26/5) | SCRUM-12 + SCRUM-26 frontend upload form Blazor + validation client | 5-6h |
| D4 (27/5) | SCRUM-25 + SCRUM-27 list page + signed URL download UI | 4-5h |
| D5 (28/5) | SCRUM-28 server-side validation + error handling toàn pipeline + add unit tests cho DocumentService | 3-4h |
| D6 (29/5) | SCRUM-14 PdfPig chunking + ONNX BGE embedding + background ingestion job | 6-8h |
| D7 (30-31/5) | SCRUM-15 search/filter subject+semester (semantic) + smoke test E2E + viết handoff `12_Session_*_Sprint1_Close.md` | 5-6h |

> **Buffer:** ngày 1/Jun cho overflow nếu D6 ONNX setup vỡ. Sprint review hard deadline 1/Jun.

### 3.3 Carryover từ session A

| # | Status |
|---|---|
| K2 | Skip per A4 — giữ default 10s grace window GoTrue |
| K5 | RESOLVED — `git status` đã verify không leak `infra/supabase/.env` + `volumes/db/data/` (xem `.gitignore` D:\FPT\summer2026\SWP391\.gitignore + nested `infra/supabase/.gitignore`) |
| K6 | RESOLVED — admin pwd redact khỏi handoff trước first commit |

---

## 4. State hiện tại (cuối session B)

### Docker
Stack `aistudyhub-supabase` profile `phase2` UP. Containers running:
- Phase 1 (7): supabase-{db, kong, auth, rest, meta, studio, analytics}
- Phase 2 (5): supabase-storage (healthy), supabase-imgproxy (healthy), supabase-vector (healthy), realtime-dev.supabase-realtime (healthy), supabase-pooler (DOWN — port 5432 conflict, expected)
- Stale (1): aistudyhub-db (Phase 1 cũ port 5433, **không xoá**)

### App
- `dotnet build` clean (commit `0e4340c`)
- App **không chạy** sau session B (đã start thử PID 28216 cho smoke setup, đã `Stop-Process` trước commit)
- 38/38 tests pass

### DB
- 6 public tables: `__EFMigrationsHistory`, `roles`, `users`, `folders`, `documents`, `document_chunks`
- 2 migrations applied: `20260524090408_InitialSupabaseAuth`, `20260525143314_AddDocumentSchema`
- 0 documents, 0 folders, 0 chunks (chưa upload thử)
- Admin user vẫn còn (`admin@aistudyhub.local`), 0 student
- ENUM `document_status` 4 values
- IVFFlat index ready
- RLS ON cho 3 bảng Phase 2

### Storage
- Bucket `documents` created (private, 50MB, 5 MIME types)
- 0 objects

### Git
- Branch: `main` (set up to track `origin/main`)
- HEAD: `0e4340c feat(documents): D2 backend upload pipeline — code-complete, smoke pending`
- Pushed: ✅ qua `c2d36cb..0e4340c  main -> main`
- Working tree: clean

---

## 5. Resume procedure (cho session C)

### 5.1 Load context (~3 phút đọc)

1. `02_Resume_Pack.md` — primary state Phase 1
2. `11_Session_2026-05-25_Sprint1_D1_D2_Handoff.md` (file này) — Sprint 1 D1-D2 bridge
3. `07_Phase2_Document_RAG_Plan.md` v-final — Phase 2 plan locked
4. (Skim) `08_Session_2026-05-24_Close_Phase1_Handoff.md` — bối cảnh đóng Phase 1

### 5.2 Verify state khớp expected

```powershell
# === Resume verify session C (2026-05-26+) ===
$ok = $true

"--- 1. Containers Phase 1 ---"
$expected = @('supabase-db','supabase-kong','supabase-auth','supabase-rest','supabase-meta','supabase-studio','supabase-analytics')
foreach ($c in $expected) {
    $st = docker inspect $c --format '{{.State.Status}}' 2>$null
    if ($st -eq 'running') { "OK: $c" } else { "FAIL: $c=$st"; $ok = $false }
}

"--- 2. Containers Phase 2 ---"
foreach ($c in @('supabase-storage','supabase-imgproxy','supabase-vector')) {
    $st = docker inspect $c --format '{{.State.Status}}' 2>$null
    if ($st -eq 'running') { "OK: $c" } else { "WARN: $c=$st (run: docker compose -f infra/supabase/docker-compose.yml --profile phase2 up -d)" }
}

"--- 3. DB tables ---"
$tables = docker exec supabase-db psql -U postgres -d postgres -t -c "SELECT table_name FROM information_schema.tables WHERE table_schema='public' ORDER BY 1;" 2>$null
foreach ($t in 'folders','documents','document_chunks','users','roles') {
    if ($tables -match "\b$t\b") { "OK: public.$t" } else { "FAIL: public.$t"; $ok = $false }
}

"--- 4. Migrations applied ---"
docker exec supabase-db psql -U postgres -d postgres -t -c "SELECT migration_id FROM public.\""__EFMigrationsHistory\"" ORDER BY 1;" 2>$null

"--- 5. Storage bucket ---"
$svcKey = (dotnet user-secrets list --project "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj" 2>&1 | Select-String 'Supabase:ServiceRoleKey').Matches[0].Groups[0].Value -replace '^Supabase:ServiceRoleKey\s*=\s*',''
try {
    $r = Invoke-RestMethod -Uri "http://localhost:8000/storage/v1/bucket/documents" -Headers @{"Authorization"="Bearer $svcKey";"apikey"=$svcKey} -Method GET
    "OK: bucket 'documents' exists, public=$($r.public), max=$($r.file_size_limit)B"
} catch { "FAIL: bucket missing"; $ok = $false }

"--- 6. Build clean ---"
$build = dotnet build "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj" --nologo -v q 2>&1 | Select-Object -Last 3
if ($build -match 'Build succeeded' -or $build -match '0 Error') { "OK: build" } else { "FAIL: build"; $ok = $false }

"--- 7. Tests pass ---"
$tests = dotnet test "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj" --nologo -v q 2>&1 | Select-Object -Last 1
if ($tests -match 'Passed!') { "OK: $tests" } else { "FAIL: tests"; $tests; $ok = $false }

"--- 8. Git clean ---"
$gitStatus = git -C "D:\FPT\summer2026\SWP391" status --short 2>&1
if (-not $gitStatus) { "OK: working tree clean" } else { "WARN: dirty"; $gitStatus }
$gitHead = git -C "D:\FPT\summer2026\SWP391" log --oneline -1
"HEAD: $gitHead (expected 0e4340c)"

""
if ($ok) { "=== STATE OK — safe to start D3 (or D2 closeout smoke first) ===" } else { "=== STATE LECH — STOP and report ===" }
```

### 5.3 Default action plan cho session C

**Override nếu Kiệt chỉ đạo khác.**

1. **D2 closeout — live smoke** (~30 phút). Section 3.1 trong file này.
2. **D3 — Blazor upload form** (SCRUM-12 + SCRUM-26):
   - `Components/Pages/Documents/Upload.razor` (MudForm + MudFileUpload + MudTextField cho subject/semester)
   - Inject `AuthApiClient` (đã có) hoặc tạo `DocumentApiClient` typed HttpClient
   - Client validation regex match server-side
   - Toast feedback qua MudSnackbar
3. **Move SCRUM-13 trên Jira → Done** sau khi smoke pass.

### 5.4 Commands cheat sheet

```powershell
# Cold start full stack Phase 2
docker compose -f "D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml" --profile phase2 up -d

# Start app dev (foreground for log visibility)
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240 --project "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj"

# Smoke upload (after login admin from Phase 1 snippet in 02 Section 8)
$pdf = "C:\path\to\test.pdf"  # bất kỳ PDF nhỏ nào
$form = @{
    file = Get-Item $pdf
    subjectCode = "SWP391"
    semester = "SU26"
}
curl.exe -sS -X POST -H "Authorization: Bearer $($login.accessToken)" `
    -F "file=@$pdf" -F "subjectCode=SWP391" -F "semester=SU26" `
    http://localhost:5240/api/documents/upload

# List
curl.exe -sS -H "Authorization: Bearer $($login.accessToken)" http://localhost:5240/api/documents

# Get + signed URL
curl.exe -sS -H "Authorization: Bearer $($login.accessToken)" http://localhost:5240/api/documents/<id>
```

---

## 6. Files Liên Kết (cập nhật)

| File | Vai trò | Status |
|---|---|---|
| `01_Architecture_Reference.md` | Architecture + schema canonical Phase 1 | ✅ Synced 2026-05-24 (Phase 2 schema chưa append) |
| `02_Resume_Pack.md` | Primary resume context Phase 1 | ✅ Synced 2026-05-24 (chưa update với entities/migration mới) |
| `03_Prompt_Playbook.md` | Template prompts | (chưa touch) |
| `04_Next_Session_Handoff.md` | OBSOLETE pre-migration | ✅ Marked 2026-05-24 |
| `05_Supabase_Local_Migration_Plan.md` | Migration plan v-final | ✅ History |
| `06_Session_2026-05-24_Build_Handoff.md` | Build log session migration sáng | ✅ Synced 2026-05-24 |
| `07_Phase2_Document_RAG_Plan.md` | **Phase 2 plan v-final** | ✅ LOCKED 2026-05-25 (L1-L10 + Section 2.1) |
| `08_Session_2026-05-24_Close_Phase1_Handoff.md` | Bridge Phase 1 → Phase 2 | ✅ Synced 2026-05-24, K6 redacted 2026-05-25 |
| `09_NUnit_Demo_Script.md` | NUnit demo prep | (chưa touch session B) |
| `10_Demo_Speaker_Notes.md` | Demo notes | (chưa touch session B) |
| `10b_Demo_Speaker_Notes_10min.md` | Demo notes 10min | (chưa touch session B) |
| `11_Session_2026-05-25_Sprint1_D1_D2_Handoff.md` | **NEW** — file này, bridge D1+D2 | ✅ Active |

---

## 7. Quick Facts (đỡ phải tra)

```
Stack:           aistudyhub-supabase profile=phase2 (12 active + 1 expected DOWN pooler)
DB:              postgres @ localhost:5432
Kong gateway:    http://localhost:8000  (auth/v1, rest/v1, storage/v1)
Storage bucket:  documents (private, 50MB, 5 MIME)
Backend:         http://localhost:5240 (NOT running, start manually)
Migration head:  20260525143314_AddDocumentSchema
Test count:      38/38 (3 smoke + 18 svc + 17 ctrl) — chưa add tests cho DocumentService (D5)
Admin login:     admin@aistudyhub.local / <pwd ở password manager + dotnet user-secrets>
Groq key:        Set in user-secrets at Groq:ApiKey
Git HEAD:        0e4340c (origin/main synced)
Phase status:    Phase 1 Auth=DONE. Sprint 1 D1+D2 backend=DONE (smoke pending).
                 Sprint 1 D3-D7 (frontend + ingestion + search) = TODO. Deadline 1 Jun.
Endpoints new:   POST /api/documents/upload (multipart)
                 GET  /api/documents?subjectCode&semester&folderId&q
                 GET  /api/documents/{id}  (kèm 5min signed URL)
                 DEL  /api/documents/{id}
```

---

**END.** Session B đóng sạch. Build clean, tests xanh, 4 commits đã push lên origin. Sẵn sàng cho session C: smoke D2 → vào D3 Blazor upload UI.
