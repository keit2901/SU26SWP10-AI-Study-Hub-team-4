# AI Study Hub v2 — Resume Pack

> **Mục đích:** Mở session mới với agent (Claude / OpenCode / Kiro / khác) → paste/đính kèm file này làm context đầu tiên → agent có đủ thông tin để **không hỏi lại** và **không phá tiến độ**.
> **Cập nhật lần cuối:** 2026-05-26 (sau Sprint 1 D1-D6 code-complete — D6 folder picker + document/folder E2E smoke PASS, commit `2a0c5d5`; parallel AI/RAG backend present uncommitted)
> **Người maintain:** Kiệt — PM Team 4 SWP391 SU26
> **Phase hoàn tất:** Phase 1 Auth (GoTrue) + **Sprint 1 D1-D6 code-complete** của Phase 2: schema + Storage bucket + document backend CRUD + signed download + Blazor upload/list/detail/delete + folder picker/move-to-folder + NUnit coverage. Sprint 2 RAG backend work đang xuất hiện song song, chưa close/commit chính thức.
> **File companion:** `previous_session/_CURRENT_SESSION.md` (session 15 live — D6 folder picker), `_CURRENT_SESSION_AI_CHATBOT_RAG.md` (parallel AI/RAG live), `14_Session_2026-05-26_Sprint1_D5_Handoff.md` (D5 backend tests), `07_Phase2_Document_RAG_Plan.md` (Phase 2 plan v-final), `rule.md` (session-progress rule).

---

## 0. Cách Dùng File Này

Khi bắt đầu session mới, gửi cho agent:
1. **File này** (`02_Resume_Pack.md`) — context tổng + state hiện tại + verification checklist. Đủ cho 95% session.
2. **`01_Architecture_Reference.md`** — chỉ khi planning Phase 2+ hoặc cần target schema 14-bảng.
3. **Mục tiêu mới của bạn** (1-2 câu, ví dụ: "tiếp tục Phase 2 — chunking + embeddings cho documents")

Sau đó nói nguyên văn:

```
Đọc 02_Resume_Pack.md trước. Chạy đúng "Resume Procedure"
ở Section 11 để verify state. Nếu state khớp expected, báo OK
rồi xử lý mục tiêu mới. Nếu lệch → STOP và báo cụ thể chỗ
lệch, KHÔNG tự sửa.
```

Quy tắc bất di bất dịch cho agent:
- **Không reopen** quyết định đã lock (Section 2) trừ khi bạn yêu cầu rõ
- **Không xóa** project cũ `AI_Study_Hub_Admin/`
- **Không init/commit git** khi chưa có yêu cầu rõ
- **Không tắt** Postgres container hay app process trừ khi bạn yêu cầu

---

## 1. Bối Cảnh Project

| Field | Value |
|---|---|
| Project name | **AI_Study_Hub_v2** (Blazor Server 8 + Web API trong cùng project) |
| Course | SWP391 SU26, FPT University |
| Team | Team 4 — Kiệt (PM), Long, Sơn, Phước, Bảo, Duy Anh |
| Project root | `D:\FPT\summer2026\SWP391\AI_Study_Hub_v2` |
| Solution file | `D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.sln` |
| Old project (kept for reference) | `D:\FPT\summer2026\SWP391\AI_Study_Hub_Admin` — **không đụng** |
| Document | `D:\FPT\summer2026\SWP391\SWP391_team_4.docx` |

---

## 2. Quyết Định Đã Lock (KHÔNG REOPEN)

| # | Item | Value |
|---|---|---|
| 1 | Backend stack | ASP.NET Core 8 + Blazor 8 Interactive Server + MudBlazor 9.4 |
| 2 | Database | Supabase Local Postgres 15 + pgvector (image `supabase/postgres`) |
| 3 | ORM | EF Core 8 + Npgsql (chỉ map `public.*`, không đụng `auth.*`) |
| 4 | Auth scheme | **Supabase GoTrue self-hosted** (HS256), domain role qua `app_metadata.role` |
| 5 | Refresh token | GoTrue native rotation + reuse-detection (grace window 10s default qua `GOTRUE_REFRESH_TOKEN_REUSE_INTERVAL`) |
| 6 | Login identifier | Email only (Phase 1) |
| 7 | Email verify / pwd reset | **Out-of-scope Phase 1** — `GOTRUE_MAILER_AUTOCONFIRM=true` |
| 8 | Username regex | `^[a-zA-Z0-9_]{3,15}$` (kiểm trong app, không trong GoTrue) |
| 9 | Password rule | Min 8 chars (GoTrue default + app validation) |
| 10 | Token TTL | Access ~1h (GoTrue default), Refresh do GoTrue quản lý |
| 11 | Default admin seed | Idempotent: GoTrue admin API tạo identity + insert profile vào `public.users`, skip nếu thiếu password hoặc đã có admin |
| 12 | Logout | GoTrue signout `?scope=global` → revoke ALL refresh tokens của user |
| 13 | CORS | Không config Phase 1 (Blazor Server same-origin) |
| 14 | HTTPS redirect | Giữ nguyên built-in |
| 15 | Storage | Supabase Storage (Phase 2 — bật `--profile phase2`) |
| 16 | AI | Groq API free tier (Llama 3.1) + Embeddings (Phase 2) |
| 17 | Vector | pgvector (extension đã sẵn trong image Supabase, chưa enable trong DB) |
| 18 | Postgres port | **5432** (Supabase Local direct, Supavisor pooler skip Phase 1) |
| 19 | Kong (API gateway) port | **8000** — endpoint `http://localhost:8000` cho GoTrue + PostgREST + Studio |
| 20 | Bỏ Phase 3 + Sub-RQ 3 (Citation accuracy) | Đã quyết 2026-05-24 — giữ focus FPT-specific |

---

## 3. Trạng Thái Code Hiện Tại

### 3.1 Tree (đã verify sau migration)

```
AI_Study_Hub_v2/
├── AI_Study_Hub_v2.csproj          ← packages: EF Core 8.0.10, Npgsql 8.0.10,
│                                     Pgvector.EntityFrameworkCore 0.2.0,
│                                     JwtBearer 8.0.10, MudBlazor 9.4.0,
│                                     Microsoft.SemanticKernel 1.76.0 (parallel RAG)
│                                     (BỎ BCrypt.Net-Next sau migration)
├── AI_Study_Hub_v2.sln
├── Program.cs                       ← DI, JwtBearer (validate GoTrue token), EF migrate-on-startup,
│                                     OnTokenValidated map app_metadata.role → ClaimTypes.Role,
│                                     SeedDefaultAdminAsync; +FolderService/FolderApiClient (D6)
│                                     +Groq/SemanticKernel AI chat DI (parallel RAG)
├── appsettings.json                 ← prod skeleton (no secrets)
├── appsettings.Development.json     ← dev: connstr port 5432 DB=postgres,
│                                     Supabase:{Url, JwtIssuer, JwtAudience, JwtSecret(secret),
│                                     AnonKey(secret), ServiceRoleKey(secret)},
│                                     Seed:DefaultAdmin (no password — secret)
├── docker-compose.db.yml            ← DEPRECATED (giữ làm rollback), header comment cảnh báo
├── Properties/launchSettings.json
├── Components/                      ← (như cũ — Blazor pages chưa rework cho refresh persistence)
├── Controllers/
│   ├── AuthController.cs            ← /api/auth/{register,login,refresh,logout,me}
│   │                                  Logout đọc access token từ HttpContext.GetTokenAsync,
│   │                                  Me đọc supabaseUserId từ sub claim + email từ ClaimTypes.Email
│   ├── DocumentsController.cs       ← [Authorize] /api/documents/{upload,GET list,GET {id},
│   │                                  PUT {id}/folder, DELETE {id}}; 50MB upload limits
│   ├── FoldersController.cs         ← D6 [Authorize] /api/folders CRUD for folder picker
│   └── AiChatController.cs          ← parallel RAG `POST /api/ai/chat/ask` (uncommitted)
├── Data/
│   ├── AppDbContext.cs              ← bỏ DbSet<RefreshToken>, dùng pgcrypto thay uuid-ossp;
│   │                                  + DbSet<Folder>, DbSet<Document>, DbSet<DocumentChunk> (Phase 2)
│   ├── AppDbContextFactory.cs       ← AddUserSecrets, throw rõ khi thiếu connstr
│   ├── Entities/
│   │   ├── User.cs, Role.cs         ← (User: bỏ Email/PasswordHash/RefreshTokens, add SupabaseUserId Guid)
│   │   ├── Folder.cs                ← Phase 2: id, owner_id (auth.users), parent_id (self FK), name, created_at
│   │   ├── Document.cs              ← Phase 2: id, owner_id, folder_id?, file_name, mime, size_bytes,
│   │   │                              storage_path, status (enum: 0=Uploading,1=Ready,2=Failed),
│   │   │                              subject_code, semester, created_at, updated_at
│   │   └── DocumentChunk.cs         ← Phase 2: id, document_id, chunk_index, page, content,
│   │                                  embedding vector(384) (pgvector), created_at
│   └── Configurations/
│       ├── User/RoleConfiguration.cs
│       ├── FolderConfiguration.cs   ← self-FK Restrict, name unique per (owner, parent)
│       ├── DocumentConfiguration.cs ← composite ix_documents_subject_semester for filter
│       └── DocumentChunkConfiguration.cs ← unique (document_id, chunk_index); ivfflat index via raw SQL
├── Migrations/
│   ├── 20260524090408_InitialSupabaseAuth.cs
│   ├── 20260524090408_InitialSupabaseAuth.Designer.cs
│   ├── 20260525143314_AddDocumentSchema.cs    ← Phase 2 schema + ivfflat cosine + RLS enable
│   ├── 20260525143314_AddDocumentSchema.Designer.cs
│   └── AppDbContextModelSnapshot.cs
├── Dtos/
│   ├── AuthDtos.cs                  ← (như cũ)
│   ├── DocumentDtos.cs              ← UploadDocumentRequest, DocumentDto, DocumentListQuery,
│   │                                  MoveDocumentFolderRequest, FolderDto/Create/Update requests
│   └── AiChatDtos.cs                ← parallel RAG ask/answer/citation DTOs (uncommitted)
├── Options/
│   ├── SupabaseOptions.cs           ← Url, JwtIssuer, JwtAudience, JwtSecret, AnonKey, ServiceRoleKey
│   ├── GroqOptions.cs               ← parallel RAG Groq/Semantic Kernel config (no API key in file)
│   └── SeedOptions.cs               ← DefaultAdmin { Email, Username, FullName, Password }
│                                     (BỎ JwtOptions.cs sau migration)
├── Services/
│   ├── AuthException.cs
│   ├── DocumentApiException.cs      ← Phase 2: thrown by DocumentApiClient (status + code + message)
│   ├── Supabase/
│   │   ├── IGoTrueClient.cs / GoTrueClient.cs / GoTrueModels.cs
│   │   └── SupabaseStorageClient.cs ← POST object/{bucket}/{path} upload, POST object/sign signed URL,
│   │                                  DELETE idempotent (404 swallow); composes <supabase_url>/storage/v1/<signedURL>
│   ├── SupabaseAuthService.cs
│   ├── DocumentService.cs           ← Phase 2: const MaxFileSizeBytes=50MB, SignedUrlTtlSeconds=300,
│   │                                  BucketName="documents"; deterministic path users/{uid_n}/{yyyy}/{guid_n}-{slug};
│   │                                  best-effort storage cleanup if DB insert fails after upload;
│   │                                  D6 MoveToFolderAsync validates folder ownership/null loose move
│   ├── FolderService.cs             ← D6 folder CRUD + owner checks + duplicate-name guard
│   ├── SemanticKernelRagChatService.cs ← parallel RAG retrieval + SK/Groq generation (uncommitted)
│   ├── AuthApiClient.cs
│   ├── DocumentApiClient.cs         ← Phase 2 typed HttpClient for Upload/List/Get/Delete + D6 MoveToFolder
│   ├── FolderApiClient.cs           ← D6 typed HttpClient for `/api/folders`
│   └── AuthSessionState.cs          ← scoped per-circuit holder (in-memory, demo-only)
├── Components/
│   ├── Layout/NavMenu.razor         ← + Study workspace label, My documents, Upload document links
│   └── Pages/
│       ├── DocumentUpload.razor     ← D3 upload form + D6 folder dropdown/create-inline;
│       │                              validates SubjectCode/Semester, 50MB + MIME whitelist,
│       │                              keeps selected folder for batch uploads
│       ├── DocumentList.razor       ← D4 list/delete + D6 folder filter, folder column,
│       │                              quick folder creation, folder counts refresh after delete
│       ├── DocumentDetail.razor     ← D4 detail/download/delete + D6 folder name display
│       │                              and move-to-folder / loose-document control
│       ├── Home.razor               ← Dashboard/Home UI polish present in workspace (parallel)
│       └── Home.razor.css           ← companion Home CSS (parallel, uncommitted)
└── wwwroot/                         ← (như cũ)

AI_Study_Hub_v2.Tests/
├── AI_Study_Hub_v2.Tests.csproj     ← NUnit 3.14 + FluentAssertions 6.12 + Moq 4.20 +
│                                     EF Core InMemory 8.0.10 + Mvc.Testing 8.0.10 + coverlet
├── SmokeTests.cs                    ← 3 sanity tests (pipeline OK, project ref compile)
├── Support/
│   └── TestDb.cs                    ← InMemory AppDbContext factory, pre-seed 2 roles;
│                                     +CreateInMemoryWithDocuments() (D5), +RAG context (parallel)
├── Services/
│   ├── SupabaseAuthServiceTests.cs  ← 18 unit tests cover Register/Login/Refresh/Logout/Me
│   ├── DocumentServiceTests.cs      ← 33 tests after D6 (29 D5 pass + 3 move tests + 1 skip)
│   ├── FolderServiceTests.cs        ← D6 7 tests: list/counts, create/update/delete, 403/404/409
│   └── SemanticKernelRagChatServiceTests.cs ← parallel RAG 5 tests (uncommitted)
└── Controllers/
    ├── AuthControllerTests.cs       ← 17 tests cover claim parsing, AuthException mapping
    ├── DocumentsControllerTests.cs  ← 22 tests after D6 (+2 move-to-folder endpoint tests)
    └── FoldersControllerTests.cs    ← D6 6 tests: list/create/update/delete + 401/mapping
```

> **Coverage status:** D5 added 50 document tests; D6 adds 18 folder/move tests. Combined workspace (D6 + parallel RAG) now verifies at **110 passed + 1 skipped + 0 failed**. D2 and D6 live smoke gate the Storage/Postgres integration surface.

**Files đã DELETE sau migration (không còn trên disk):**
`Services/PasswordHasher.cs`, `Services/JwtTokenService.cs`, `Services/RefreshTokenService.cs`, `Services/AuthService.cs`, `Data/Entities/RefreshToken.cs`, `Data/Configurations/RefreshTokenConfiguration.cs`, `Options/JwtOptions.cs`, migration cũ `20260523183927_InitialCreate.*`.

### 3.2 Build status

```
dotnet build (sln)
→ Build succeeded. 0 Warning(s). 0 Error(s).  (combined workspace final verify 2026-05-26T11:58Z)
dotnet test (sln) → Passed! 110/111 + 1 skipped (Duration: ~1s)
            ├─ SmokeTests: 3 (pipeline sanity)
            ├─ SupabaseAuthServiceTests: 18
            ├─ AuthControllerTests: 17
            ├─ DocumentServiceTests: 33 (includes D6 move-to-folder; 1 documented ILike skip)
            ├─ DocumentsControllerTests: 22 (includes D6 move endpoint)
            ├─ FolderServiceTests: 7
            ├─ FoldersControllerTests: 6
            └─ SemanticKernelRagChatServiceTests: 5 (parallel RAG)
```

### 3.3 Database state (Postgres `postgres` DB on container `supabase-db` @ localhost:5432)

Schemas: `public`, `auth` (GoTrue), `storage`, `_supabase`, `extensions`, `realtime`, `pgsodium`, ...

Tables in `public`:
```
public | __EFMigrationsHistory | table | postgres
public | roles                 | table | postgres   -- RLS ON
public | users                 | table | postgres   -- RLS ON
public | folders               | table | postgres   -- RLS ON  (Phase 2 D1)
public | documents             | table | postgres   -- RLS ON  (Phase 2 D1)
public | document_chunks       | table | postgres   -- RLS ON  (Phase 2 D1)
```

Migrations applied:
- `20260524090408_InitialSupabaseAuth`
- `20260525143314_AddDocumentSchema`  ← Phase 2 D1 (folders + documents + document_chunks + ivfflat cosine `lists=100` + RLS enable)

Seeded data:
- 2 roles: `Admin`, `Student` (`public.roles`)
- 1 admin: `admin@aistudyhub.local` (auth.users + public.users mirror, role=Admin)
- DB clean — `public.documents`, `public.document_chunks`, `public.folders` all 0 rows after D6 document+folder E2E cleanup (2026-05-26T12:20Z smoke).

Storage bucket (`http://localhost:8000/storage/v1`):
- `documents` — private=true, file_size_limit=50MB, 5 allowed MIME (pdf, docx, pptx, doc, ppt — verified live via Storage REST 2026-05-26T09:43Z; matches `DocumentService.AllowedMimeTypes` + `DocumentApiClient.AllowedMimeTypes` + `DocumentUpload.razor` AcceptAttr 4-way). Created via Storage REST POST `/bucket`.

Extensions enabled in `postgres` DB: `vector 0.8.0`, `pgcrypto`, `uuid-ossp` (sẵn từ image).

Indexes on `public.document_chunks`:
- `PK_document_chunks` (PK)
- `IX_document_chunks_document_id` (FK index)
- `IX_document_chunks_document_id_chunk_index` (unique composite)
- `ix_document_chunks_embedding` (ivfflat cosine, lists=100 — added via raw SQL in migration)

Index on `public.documents`:
- `ix_documents_subject_semester` (composite for filter perf)

### 3.4 User Secrets (project `f7443cc6-0949-4e12-9bab-2badfa96be5a`)

```
ConnectionStrings:Postgres   = Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=<từ infra/supabase/.env POSTGRES_PASSWORD>
Supabase:JwtSecret           = <từ infra/supabase/.env JWT_SECRET, >=32 chars>
Supabase:AnonKey             = <từ infra/supabase/.env ANON_KEY>
Supabase:ServiceRoleKey      = <từ infra/supabase/.env SERVICE_ROLE_KEY>
Seed:DefaultAdmin:Password   = <generated, lưu ở C:\Users\pc\AppData\Local\Temp\opencode\admin-pwd.txt>
```

> Raw values lưu ở `D:\FPT\summer2026\SWP391\infra\supabase\.env` (host, gitignored). **KHÔNG commit.** Admin pwd: đã chuyển sang password manager của Kiệt 2026-05-24 (file tạm `admin-pwd.txt` đã xoá — K4 trong file 06). Pwd vẫn còn trong `dotnet user-secrets` (`Seed:DefaultAdmin:Password`) cho seed idempotent — nếu cần lookup, chạy `dotnet user-secrets list --project AI_Study_Hub_v2/AI_Study_Hub_v2.csproj`.

---

## 4. Phases — Done / Pending

| Phase | Scope | Status |
|---|---|---|
| 1 | Skeleton Blazor project + sln + verify build | ✅ Done |
| 2 | Copy selective Components, wwwroot, Dtos, AuthController, launchSettings | ✅ Done |
| 3 | NuGet packages + Data layer (Entities, DbContext, Configurations) | ✅ Done |
| 3b | Custom JWT Auth + RefreshTokens (Phase 1 cũ) | ✅ Done → **REPLACED bởi Supabase GoTrue** |
| 4 | appsettings + Program.cs (DI, JWT, migrate-on-startup, seed) | ✅ Done (refactored cho GoTrue) |
| 5 | docker-compose.db.yml + Postgres up + EF migrations | ✅ Done → **REPLACED bởi Supabase Local stack** |
| 6 | DTOs + AuthController endpoints | ✅ Done |
| 7 | User Secrets + smoke test 5 endpoints + Blazor SSR pages | ✅ Done |
| 7b | **Migrate sang Supabase Local Phase 1 (15/16 step plan v-final)** | ✅ Done 2026-05-24 |
| 7c | **Fix D6 — `/me` trả email từ JWT claim** | ✅ Done 2026-05-24 |
| 8 | Phase 2 Sprint 1 D1 — Schema + Storage bucket (folders, documents, document_chunks, ivfflat, RLS, bucket `documents`) | ✅ Done 2026-05-25 |
| 9 | Phase 2 Sprint 1 D2 — Document backend pipeline (DocumentsController + DocumentService + SupabaseStorageClient) — code + smoke E2E | ✅ Done 2026-05-26 (smoke 8/8 GREEN) |
| 10 | Phase 2 Sprint 1 D3 — Blazor upload form (`DocumentApiClient` + `/documents/upload` page + nav link) | ✅ Done 2026-05-26 (code-complete; manual UI smoke deferred to Kiệt) |
| 11 | Phase 2 Sprint 1 D4 — List/detail/delete UI (`/documents` + `/documents/{id}`) | ✅ Done 2026-05-26 (code-complete; manual UI smoke deferred; folder picker split → D6) |
| 12 | Phase 2 Sprint 1 D5 — Backend tests for DocumentService + DocumentsController (SCRUM-28) | ✅ Done 2026-05-26 (50 new tests, 87/88 pass + 1 documented skip) |
| 13 | Phase 2 Sprint 1 D6 — Demo polish + Folder picker (`FoldersController` + `IFolderService` + `FolderApiClient` + dropdown/filter/move UI) | ✅ Done 2026-05-26 commit `2a0c5d5` (E2E smoke PASS) |
| 14 | Phase 2 Sprint 2 — Chunking + embeddings + RAG retrieve + Groq generation | ⏳ Pending (parallel AI/RAG backend surface present uncommitted) |

### 4.1 Smoke Test Results — Phase 1 (Supabase GoTrue)

| # | Test | Endpoint | Result |
|---|---|---|---|
| 1 | Login admin | `POST /api/auth/login` | 200, role=Admin (qua app_metadata mapping) |
| 2 | Get current user | `GET /api/auth/me` (Bearer) | 200, **email='admin@aistudyhub.local'** (sau fix D6), role=Admin |
| 3 | Register Student | `POST /api/auth/register` | 200, autoconfirm on, role=Student |
| 4 | Refresh rotation | `POST /api/auth/refresh` | 200, new access + refresh |
| 5 | Refresh reuse trong 10s grace window | Replay RT | 200 (GoTrue native, **deviation D5**) |
| 6 | Refresh reuse sau 10s | Replay RT | 401 + chain-revoke |
| 7 | Logout (scope=global) | `POST /api/auth/logout` (Bearer) | 204 |
| 8 | Refresh sau logout | `POST /api/auth/refresh` | 401 invalid_refresh_token |
| 9 | `dotnet test` | NUnit | 3/3 pass |

> **Deviation D5 chi tiết:** GoTrue có `GOTRUE_REFRESH_TOKEN_REUSE_INTERVAL` mặc định 10s — RT cũ vẫn được chấp nhận trong cửa sổ này (cover network retry). Sau cửa sổ → 401 + chain-revoke ALL RTs của user (bảo mật hơn behavior cũ). Có thể set `=0` trong `infra/supabase/.env` nếu muốn match đúng plan v-final.

### 4.2 Smoke Test Results — Sprint 1 D2 (Documents backend, 2026-05-26T04:19Z)

| # | Step | Endpoint / Action | Result |
|---|---|---|---|
| 1 | Boot app | `dotnet run --urls http://localhost:5240` | (re-used existing PID 9928 already running, post-D1+D2 commit) |
| 2 | Login admin | `POST /api/auth/login` | 200, accessToken returned |
| 3 | Upload | `POST /api/documents/upload` (multipart, `smoke_small.pdf` 535B, SWP391/SU26) | 201, id=`a8182289-…`, status=Ready |
| 4 | List | `GET /api/documents` | 1 entry, matches uploaded |
| 5 | Get one | `GET /api/documents/{id}` | DTO + 5min signed URL pointing to `localhost:8000/storage/v1/object/sign/documents/users/{uid_n}/2026/{guid_n}-smoke_small.pdf?token=…` |
| 6 | Anon download | GET signed URL (no auth header) | 200, 535 bytes, `Content-Type: application/pdf`, byte-equal to upload (Kong storage route reachable) |
| 7 | Delete | `DELETE /api/documents/{id}` | 204 |
| 8 | Cleanup verify | `GET /api/documents`, Storage REST `/object/list/documents`, `psql SELECT count(*) FROM public.documents` | API=0, bucket=0, DB=0 |

> Both D2 known-unknowns from handoff 11 §3.1 cleared:
> - `[FromForm] UploadDocumentRequest + IFormFile file` binding works on same multipart body (#1).
> - Signed URL from Kong is reachable from browser-equivalent client without auth header (#4).
> Commit: `0245045 test(documents): D2 smoke E2E pass — upload/list/get/signed-download/delete verified live`.

### 4.3 D3 Blazor Upload Form (2026-05-26T04:27Z, manual browser UI smoke deferred)

| Item | Value |
|---|---|
| Page | `/documents/upload` (`@rendermode InteractiveServer`, auth-gated) |
| Validation | Client: `SubjectCode` `^[A-Z]{3}[0-9]{3}$`, `Semester` `^(SP|SU|FA|WI)[0-9]{2}$`, 50MB cap, MIME whitelist (with extension fallback for browsers dropping Office MIME) |
| Upload mechanism | `IBrowserFile.OpenReadStream(50MB)` → `DocumentApiClient.UploadAsync` |
| MudFileUpload | v9 `Hidden=true` + companion `MudButton OnClick=OpenFilePickerAsync` (v8 `<ActivatorContent>` removed in 9.4) |
| Error mapping | `DocumentApiException.StatusCode` 401/413/415 → friendly `MudAlert` copy |
| Build/Test gate | 0 warning, 0 error; 38/38 tests pass (no regression) |
| Commit | `8454b0d feat(documents): D3 Blazor upload form (SCRUM-12/26) — code-complete` |

### 4.4 D4 Blazor List/Detail/Delete UI (2026-05-26T09:34Z, manual browser UI smoke deferred)

| Item | Value |
|---|---|
| List page | `/documents` (`@rendermode InteractiveServer`, auth-gated) — `MudTable` with `subject/semester/text` filters + `MudTablePager` (10/25/50/100); `ConfirmDialog`-driven delete; status chips, MIME-aware icons, file-size formatter |
| Detail page | `/documents/{Id:guid}` — detail card; 5min signed-URL "Open file" button + "Get fresh link" refresh; delete via ConfirmDialog → nav back to `/documents`; `OnParametersSetAsync` reload on id change |
| Client API | `DocumentApiClient.GetAsync(id)` + `DocumentApiClient.DeleteAsync(id)` (added) |
| NavMenu | + "My documents" link before existing "Upload document" (auth-only) |
| Reused | `Components/Admin/Shared/ConfirmDialog.razor` (pre-existing, supports type-to-confirm pattern) |
| Error mapping | `DocumentApiException.StatusCode` 401 (clear session + /login), 403, 404, fallback `ex.Message`; network errors via `Snackbar.Severity.Error` |
| Build/Test gate | 0 warning, 0 error; 38/38 tests pass (no regression) |
| Commit | `50a8122 feat(documents): D4 list/detail/delete UI (SCRUM-15/25) — code-complete` |
| Deferred | Folder picker → D6 (needs `FoldersController` + `IFolderService` + `FolderApiClient` + dropdown); manual browser smoke → Kiệt |
| Origin note | Code came in as drift between session 12 close (07:20Z) and session 13 open (07:33Z) — reviewed, build/test green, accepted by Kiệt 09:33Z (D-2026-05-26-02). Lesson: extend session-open verify to include `git status --porcelain` mismatch check, not just tree-clean. |

### 4.5 D5 Backend tests for DocumentService + DocumentsController (2026-05-26T09:54Z)

| Item | Value |
|---|---|
| Service tests | `DocumentServiceTests.cs` — 30 NUnit tests (29 pass, 1 documented skip): Upload happy + filename sanitisation + 4 error branches (404 user_not_found, 403 user_inactive, 400 empty_file, 413 file_too_large) + 415 mime matrix x4 + allowed-mime regression matrix x5 + folder ownership 404/accept + storage upload throws → bubble + zero rows; List filter/order/isolation; GetById happy w/ signed URL + 404; Delete happy + 404 + storage 503 still removes row |
| Controller tests | `DocumentsControllerTests.cs` — 20 NUnit tests: Upload (claim parsing NameId + sub fallback, missing_file 400 for null + empty file, missing_user_id 401 on non-GUID sub, DocumentException matrix x6 → status+code mapping, unexpected → 500); List/GetById/Delete (happy + DocumentException → status + unexpected → 500) |
| Test harness | `Support/TestDb.cs` extended with `CreateInMemoryWithDocuments()` + `TestDocsDbContext` — keeps Document + Folder mapped (only DocumentChunk Ignored for pgvector); InMemory persists Document.Status enum as int |
| Documented gap | `EF.Functions.ILike` Q-text branch in ListAsync has no InMemory translation → `[Ignore]`-d with reason; D2 live smoke covers it |
| Build/Test gate | 0 warning, 0 error; **87/88 pass + 1 skipped, 0 failed** in 1.8s (was 38/38) |
| Commit | `9dce4a0 test(documents): D5 backend tests for DocumentService + DocumentsController (SCRUM-28)` |

### 4.6 D6 Folder picker + demo polish (2026-05-26T12:20Z, committed `2a0c5d5`)

| Item | Value |
|---|---|
| Backend | `FoldersController` + `IFolderService`/`FolderService` for authorized folder list/create/update/delete; duplicate folder name returns 409; inactive user returns 403; owner isolation returns 404 |
| Document move | `PUT /api/documents/{id}/folder` + `DocumentService.MoveToFolderAsync` moves document into owned folder or `null` loose document |
| Blazor upload | `/documents/upload` loads folders, offers folder dropdown, inline folder creation, and keeps selected folder for batch uploads |
| Blazor list | `/documents` adds folder filter, folder column, quick folder creation, and folder count refresh after delete |
| Blazor detail | `/documents/{id}` displays folder name and supports move-to-folder / move-to-loose |
| Tests | +18 D6 tests: `FolderServiceTests` 7, `FoldersControllerTests` 6, `DocumentServiceTests` +3 move tests, `DocumentsControllerTests` +2 move endpoint tests |
| E2E smoke | PASS: admin login -> create folder -> upload PDF with FolderId -> list folder -> detail signed URL -> move to loose -> delete doc/folder -> final absent |
| Build/Test gate | 0 warning, 0 error; combined workspace **110 passed + 1 skipped + 0 failed** |
| Commit | `2a0c5d5 feat(documents): D6 folder picker and move-to-folder flow` |

---

## 5. Cách Chạy Lại Từ Đầu (cold start)

Chỉ cần khi: máy reboot, container đã `docker compose down`, hoặc dotnet process đã chết.

```powershell
# 1. Start Supabase Local stack (7 services Phase 1)
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml up -d

# 2. Wait healthy (~10-30s) — verify
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml ps

# 3. Verify GoTrue + DB
docker exec supabase-db psql -U postgres -d postgres -c "SELECT count(*) FROM public.users;"
# expect 1 (admin)  — trước 2026-05-24 còn 2 (admin + student4090 test, đã xoá)

# 4. Restore (nếu obj/ bị xóa) + verify build
cd D:\FPT\summer2026\SWP391\AI_Study_Hub_v2
dotnet restore
dotnet build

# 5. Start app — DEV environment để load User Secrets
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240
```

App listening: `http://localhost:5240` (HTTP only, Phase 1 không config HTTPS cert dev).

Studio admin: `http://localhost:8000` → login `supabase / <DASHBOARD_PASSWORD từ infra/supabase/.env>`.

Login UI: mở browser → `http://localhost:5240/login` → nhập `admin@aistudyhub.local` / `<pwd từ password manager>`.

### 5.1 Background-run wrapper (đã tạo, để start ẩn)

`C:\Users\pc\AppData\Local\Temp\opencode\run-v2.cmd`:
```
@echo off
setlocal
set ASPNETCORE_ENVIRONMENT=Development
cd /d "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2"
dotnet run --no-launch-profile --urls http://localhost:5240
```

Logs: `C:\Users\pc\AppData\Local\Temp\opencode\v2-app.log` + `v2-app.err.log`
PID: `C:\Users\pc\AppData\Local\Temp\opencode\v2-app.pid`

---

## 6. Cách Stop Sạch (cleanup)

```powershell
# Stop app
$pidFile = "C:\Users\pc\AppData\Local\Temp\opencode\v2-app.pid"
if (Test-Path $pidFile) {
    $procId = (Get-Content $pidFile).Trim()
    Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
    Remove-Item $pidFile -Force
}
# Kill any leftover dotnet on port 5240
Get-NetTCPConnection -LocalPort 5240 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

# Stop Supabase Local stack (giữ data volumes)
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml stop
# hoặc remove containers nhưng giữ volumes:
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml down

# DESTRUCTIVE — chỉ khi muốn reset từ đầu (xoá luôn data volumes)
# docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml down -v
```

---

## 7. Endpoints Reference

| Method | Path | Auth | Body | 200 Response |
|---|---|---|---|---|
| POST | `/api/auth/register` | None | `{email, username, fullName, password}` | `AuthResponse` |
| POST | `/api/auth/login` | None | `{email, password}` | `AuthResponse` |
| POST | `/api/auth/refresh` | None | `{refreshToken}` | `AuthResponse` (rotated) |
| POST | `/api/auth/logout` | Bearer | (none) | 204 NoContent |
| GET | `/api/auth/me` | Bearer | (none) | `UserDto` |

`AuthResponse` shape:
```json
{
  "accessToken": "...",
  "refreshToken": "base64-64-bytes",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "expiresAt": "2026-05-23T20:37:52Z",
  "user": { "id":"<guid>","email":"...","username":"...","fullName":"...","role":"Admin|Student","isActive":true,"createdAt":"..." }
}
```

Error shape (`ApiErrorResponse`):
```json
{ "code": "invalid_credentials", "message": "...", "errors": null }
```

Error codes hiện có: `invalid_credentials`, `user_not_found`, `username_taken`, `profile_missing`, `user_inactive`, `invalid_refresh_token`, `missing_refresh_token`, `missing_access_token`, `missing_user_id`, `gotrue_no_user`, `role_not_seeded`, `unexpected_error`. Lỗi do GoTrue trả về (email trùng, password yếu, ...) sẽ được wrap thành `AuthException` với message từ GoTrue → giữ nguyên status code (400/422 từ GoTrue → bubble lên client).

---

## 8. Smoke Test Snippet (PowerShell, paste-able)

```powershell
# 0. Lấy admin password từ user-secrets (đã move ra password manager, vẫn lưu trong secrets cho seed)
$secrets = dotnet user-secrets list --project "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj"
$pwd = ($secrets | Select-String -Pattern '^Seed:DefaultAdmin:Password\s*=\s*(.+)$').Matches[0].Groups[1].Value.Trim()
# Hoặc paste tay: $pwd = "<pwd từ password manager>"

# 1. Login admin
$body = @{ email = "admin@aistudyhub.local"; password = $pwd } | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText("$env:TEMP\login.json", $body, [System.Text.UTF8Encoding]::new($false))
$login = curl.exe -sS -X POST -H "Content-Type: application/json" --data "@$env:TEMP\login.json" http://localhost:5240/api/auth/login | ConvertFrom-Json
"Login OK. Role=$($login.user.role) Email=$($login.user.email)"

# 2. /me — phải có email != "" sau fix D6
$me = curl.exe -sS -H "Authorization: Bearer $($login.accessToken)" http://localhost:5240/api/auth/me | ConvertFrom-Json
"/me OK. Username=$($me.username) Email=$($me.email)"

# 3. Refresh rotate
$body2 = @{ refreshToken = $login.refreshToken } | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText("$env:TEMP\refresh.json", $body2, [System.Text.UTF8Encoding]::new($false))
$r2 = curl.exe -sS -X POST -H "Content-Type: application/json" --data "@$env:TEMP\refresh.json" http://localhost:5240/api/auth/refresh | ConvertFrom-Json
"Refresh OK. New refresh issued."

# 4. Logout (scope=global)
curl.exe -sS -X POST -H "Authorization: Bearer $($r2.accessToken)" http://localhost:5240/api/auth/logout
"Logout OK"
```

---

## 9. Schema (Hiện Có Trong DB)

Phase 1 mirror profile + Phase 2 D1 schema (folders / documents / document_chunks). Identity (password, refresh, session) sống trong `auth.*` do GoTrue quản lý — **app KHÔNG đụng trực tiếp**, mọi thao tác qua HTTP API. Schema 14-bảng đầy đủ là Phase 2+ Sprint 2+ (chunks done, citations / sessions / messages / quiz / token-budget chưa add).

```sql
-- public.roles (RLS ON)
id              UUID PRIMARY KEY DEFAULT gen_random_uuid()
role_name       TEXT UNIQUE NOT NULL  -- 'Admin' | 'Student'
description     TEXT
created_at      TIMESTAMPTZ NOT NULL

-- public.users (RLS ON) — KHÔNG có email/password_hash
id                  UUID PRIMARY KEY DEFAULT gen_random_uuid()
role_id             UUID NOT NULL REFERENCES public.roles(id)
supabase_user_id    UUID NOT NULL UNIQUE REFERENCES auth.users(id) ON DELETE CASCADE
username            TEXT UNIQUE NOT NULL
full_name           TEXT NOT NULL
total_tokens_used   INT NOT NULL DEFAULT 0
is_active           BOOLEAN NOT NULL DEFAULT TRUE
created_at          TIMESTAMPTZ NOT NULL
updated_at          TIMESTAMPTZ NOT NULL
-- index unique trên supabase_user_id

-- public.folders (RLS ON) — Phase 2 D1
id           UUID PRIMARY KEY DEFAULT gen_random_uuid()
owner_id     UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE
parent_id    UUID NULL REFERENCES public.folders(id) ON DELETE Restrict
name         TEXT NOT NULL
created_at   TIMESTAMPTZ NOT NULL
-- unique (owner_id, parent_id, name)

-- public.documents (RLS ON) — Phase 2 D1
id              UUID PRIMARY KEY DEFAULT gen_random_uuid()
owner_id        UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE
folder_id       UUID NULL REFERENCES public.folders(id) ON DELETE SET NULL
file_name       TEXT NOT NULL
mime_type       TEXT NOT NULL
size_bytes      BIGINT NOT NULL
storage_path    TEXT NOT NULL  -- "users/{uid_n}/{yyyy}/{guid_n}-{slug}"
status          INT NOT NULL  -- 0=Uploading, 1=Ready, 2=Failed
subject_code    TEXT NOT NULL  -- e.g. SWP391
semester        TEXT NOT NULL  -- e.g. SU26
created_at      TIMESTAMPTZ NOT NULL
updated_at      TIMESTAMPTZ NOT NULL
-- composite ix_documents_subject_semester (subject_code, semester)

-- public.document_chunks (RLS ON) — Phase 2 D1 (populated in Sprint 2)
id            UUID PRIMARY KEY DEFAULT gen_random_uuid()
document_id   UUID NOT NULL REFERENCES public.documents(id) ON DELETE CASCADE
chunk_index   INT NOT NULL
page          INT NULL
content       TEXT NOT NULL
embedding     vector(384) NULL  -- pgvector; ivfflat cosine index lists=100
created_at    TIMESTAMPTZ NOT NULL
-- unique (document_id, chunk_index)
-- ix_document_chunks_embedding USING ivfflat (embedding vector_cosine_ops) WITH (lists=100)

-- auth.* (GoTrue managed — đừng tạo migration đụng vào)
auth.users          -- id, email, encrypted_password, email_confirmed_at, raw_app_meta_data, raw_user_meta_data, ...
auth.identities     -- provider linkage
auth.refresh_tokens -- rotation + reuse detection (10s grace window)
auth.sessions       -- aal level, factor_id

-- storage.* (Supabase Storage)
-- bucket 'documents': private=true, file_size_limit=50MB,
--   allowed_mime_types = pdf, doc, docx, txt, md
```

---

## 10. Limitations / Known Constraints

- **Blazor session persistence:** `AuthSessionState` lưu in-memory per circuit. **Refresh trang = logout.** Đây là chủ ý cho Phase 1 demo, không phải bug. Nâng lên localStorage/cookie là việc của Phase 2.
- **Access token sau logout:** vẫn valid tới `exp` vì JWT stateless (không có deny-list). Đúng design. Nếu cần revoke ngay → Phase 2 thêm jti deny-list ở Redis hoặc DB.
- **HTTPS:** chưa config dev cert. App chạy HTTP-only port 5240. Production sẽ dùng reverse proxy (Nginx/IIS) terminate TLS.
- **Refresh token reuse grace window 10s:** GoTrue native, **không phải bug** (xem D5 trong file 06). App đã handle qua AuthException 401 sau cửa sổ. Set `GOTRUE_REFRESH_TOKEN_REUSE_INTERVAL=0` trong `infra/supabase/.env` nếu muốn behavior strict.
- **Stack cũ Postgres `aistudyhub-db @ 5433`:** đã `docker compose stop`, **không xoá**. Volume `aistudyhub-db_db-data` còn nguyên. Backup `D:\FPT\summer2026\SWP391\backups\aistudyhub-db_backup_20260524.tgz` (giữ tới 2026-05-31) cho rollback. `docker-compose.db.yml` đã mark deprecated.
- **Stack Admin (`AI_Study_Hub_Admin/supabase-local`):** đã `docker compose down`, volumes giữ nguyên. Không đụng.
- **pgvector extension:** đã có sẵn trong DB `postgres` (v0.8.0), chưa enable trong app context. Sẽ enable khi vào Phase 2.
- **EF tool version:** máy có `dotnet-ef 9.0.9` (global). Project target net8.0, vẫn dùng được. Nếu lỗi → cài lại 8.0.x: `dotnet tool update --global dotnet-ef --version 8.0.10`.
- **`infra/supabase/volumes/db/data/`:** chứa Postgres data, đã ignore qua `infra/supabase/.gitignore`. Khi `git add infra/`, double check `git status` để chắc chắn không leak.

---

## 11. Resume Procedure (Verify Trước Khi Code)

Lệnh **một-cú** để agent kiểm tra state khớp expected. Copy-paste vào PowerShell:

```powershell
# === AI_Study_Hub_v2 (Supabase Local Phase 1) health check ===
$ok = $true

"--- 1. Supabase Local containers ---"
$expected = @('supabase-db','supabase-kong','supabase-auth','supabase-rest','supabase-meta','supabase-studio','supabase-analytics')
foreach ($c in $expected) {
    $st = docker inspect $c --format '{{.State.Status}}' 2>$null
    if ($st -eq 'running') { "OK: $c running" } else { "FAIL: $c = '$st'"; $ok = $false }
}

"--- 2. Postgres port 5432 ---"
$p5432 = Test-NetConnection -ComputerName localhost -Port 5432 -InformationLevel Quiet -WarningAction SilentlyContinue
if ($p5432) { "OK: 5432 listening" } else { "FAIL: 5432 not listening"; $ok = $false }

"--- 3. Kong gateway port 8000 ---"
$p8000 = Test-NetConnection -ComputerName localhost -Port 8000 -InformationLevel Quiet -WarningAction SilentlyContinue
if ($p8000) { "OK: 8000 listening" } else { "FAIL: 8000 not listening"; $ok = $false }

"--- 4. App port 5240 ---"
$p5240 = Test-NetConnection -ComputerName localhost -Port 5240 -InformationLevel Quiet -WarningAction SilentlyContinue
if ($p5240) {
    "OK: 5240 listening"
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:5240/login" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { "OK: /login renders ($($r.Content.Length) bytes)" } else { "FAIL: /login status $($r.StatusCode)"; $ok = $false }
    } catch { "FAIL: cannot reach app: $($_.Exception.Message)"; $ok = $false }
} else { "WARN: 5240 not listening — app not started (run Section 5)" }

"--- 5. User secrets ---"
$secrets = dotnet user-secrets list --project "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj" 2>&1
foreach ($k in 'Supabase:JwtSecret','Supabase:AnonKey','Supabase:ServiceRoleKey','Seed:DefaultAdmin:Password','ConnectionStrings:Postgres') {
    if ($secrets -match [regex]::Escape($k)) { "OK: secret '$k' present" } else { "FAIL: secret '$k' missing"; $ok = $false }
}

"--- 6. DB tables ---"
$tables = docker exec supabase-db psql -U postgres -d postgres -t -c "SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_schema IN ('public','auth') ORDER BY 1;" 2>$null
foreach ($t in 'public.users','public.roles','public.__EFMigrationsHistory','auth.users','auth.refresh_tokens') {
    if ($tables -match [regex]::Escape($t)) { "OK: $t exists" } else { "FAIL: $t missing"; $ok = $false }
}

"--- 7. Admin seeded ---"
$admin = docker exec supabase-db psql -U postgres -d postgres -t -c "SELECT au.email FROM public.users u JOIN public.roles r ON u.role_id=r.id JOIN auth.users au ON au.id=u.supabase_user_id WHERE r.role_name='Admin';" 2>$null
if ($admin -match 'admin@aistudyhub.local') { "OK: admin user exists" } else { "FAIL: admin not seeded"; $ok = $false }

"--- 8. Build clean ---"
$build = dotnet build "D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.csproj" --nologo -v q 2>&1 | Select-Object -Last 5
if ($build -match 'Build succeeded') { "OK: build clean" } else { "FAIL: build issues"; $build; $ok = $false }

""
if ($ok) { "=== STATE OK — safe to proceed ===" } else { "=== STATE LECH — STOP and report ===" }
```

Expected output: tất cả `OK:`, kết thúc `=== STATE OK — safe to proceed ===`.

Nếu **app chưa chạy** (5240 not listening): chỉ là chưa start, không phải state lệch. Run Section 5 để start lên.

---

## 12. Phase 2 — Backlog (Sprint 1 D1-D6 code-complete, Sprint 2 RAG next)

### Sprint 1 — Document Management (6/6 ✅ code-complete)

| Day | Scope | Status |
|---|---|---|
| D1 | EF schema (folders/documents/document_chunks) + ivfflat + RLS + Storage bucket `documents` | ✅ commit `c2d36cb` |
| D2 | Backend `DocumentsController` + `DocumentService` + `SupabaseStorageClient` + smoke E2E live | ✅ commit `0245045` (smoke green) |
| D3 | Blazor `DocumentApiClient` + `/documents/upload` page + nav link | ✅ commit `8454b0d` (manual UI smoke deferred) |
| D4 | Blazor list/detail/delete UI (`/documents` + `/documents/{id}`) — `MudTable` + filters + `ConfirmDialog` delete + signed-URL "Open file" | ✅ commit `50a8122` (manual UI smoke deferred; folder picker split → D6) |
| D5 | NUnit tests for `DocumentService` (unit, mock IStorageClient + EF InMemory) + `DocumentsController` (unit-level claim + exception mapping) — SCRUM-28 | ✅ commit `9dce4a0` (50 new tests, 87/88 pass + 1 documented skip) |
| D6 | Demo polish + folder picker (`FoldersController`, `FolderService`, `FolderApiClient`, Upload/List/Detail dropdown/filter/move) | ✅ commit `2a0c5d5`; E2E smoke PASS 2026-05-26T12:20Z |

### Sprint 2 — RAG pipeline (planned)

- Chunking ingestion worker (size 500-1000 tokens, 10-20% overlap, page metadata for citation) — triggered by `documents.status=Ready`
- Embeddings via local sentence-transformers `all-MiniLM-L6-v2` (384-dim, matches schema) — confirm with Kiệt before locking
- Vector search endpoint (`POST /api/rag/search` — top-k cosine, return chunks + page citations)
- Generation via Groq Llama 3.1 free tier — prompt template with retrieved chunks
- Token-budget tracking → `public.users.total_tokens_used`
- Add bảng `chat_sessions, chat_messages, citations` qua migration `AddChatSchema`

Trước khi bắt đầu Sprint 2, cần Kiệt confirm:
- Embedding model + dim — schema khoá `vector(384)`, đổi model phải migration
- Groq API key đã có chưa, quota free tier
- Chunking config (size, overlap) cuối cùng
- Citation format (page-level? chunk-level? cả hai?)

---

## 13. Files Liên Quan

| File | Vai trò |
|---|---|
| `previous_session/02_Resume_Pack.md` | **File này** — primary resume context, đọc mỗi session mới |
| `previous_session/01_Architecture_Reference.md` | Target schema + phase roadmap (đã refresh sau migration) |
| `previous_session/03_Prompt_Playbook.md` | Template prompt sẵn cho session mới |
| `previous_session/05_Supabase_Local_Migration_Plan.md` | Plan v-final của migration session (Phase 1) |
| `previous_session/06_Session_2026-05-24_Build_Handoff.md` | Build log session migration |
| `previous_session/07_Phase2_Document_RAG_Plan.md` | **Phase 2 plan v-final** — schema + chunking + embeddings + RAG roadmap |
| `previous_session/08_Session_2026-05-24_Close_Phase1_Handoff.md` | Phase 1 closeout |
| `previous_session/09_NUnit_Demo_Script.md` + `10_*` + `10b_*` | Demo speaker notes |
| `previous_session/11_Session_2026-05-25_Sprint1_D1D2_Handoff.md` | Sprint 1 D1+D2 code-complete handoff (canonical) |
| `previous_session/11a_Session_2026-05-25_Sprint1_D1_D2_Handoff_superseded.md` | Older near-dup of 11, marked SUPERSEDED |
| `previous_session/12_Session_2026-05-26_Sprint1_D2smoke_D3_Handoff.md` | D2 smoke E2E green + D3 upload form code-complete |
| `previous_session/13_Session_2026-05-26_Sprint1_D4_Handoff.md` | D4 list/detail/delete UI code-complete |
| `previous_session/_CURRENT_SESSION.md` | **Current live** — session 15 D6 folder picker + E2E smoke; not closed/committed yet |
| `previous_session/_CURRENT_SESSION_AI_CHATBOT_RAG.md` | Parallel live — AI/RAG backend work; not closed/committed yet |
| `previous_session/14_Session_2026-05-26_Sprint1_D5_Handoff.md` | Latest closed handoff — D5 backend tests for DocumentService + DocumentsController (SCRUM-28) |
| `previous_session/rule.md` | **Session-progress tracking rule** (mandatory for all agents) |
| `previous_session/04_Next_Session_Handoff.md` | OBSOLETE — viết trước migration, giữ làm history |
| `previous_session/archive/previous_session_raw_transcript.md` | Raw Q&A transcript session đầu |
| `infra/supabase/docker-compose.yml` | Supabase Local stack (Phase 1 default + `--profile phase2` for storage/realtime/imgproxy/vector/supavisor) |
| `infra/supabase/.env` | Secrets Supabase Local (gitignored) |
| `AI_Study_Hub_Project_Overview.md` | Overview cũ của nhóm, cần update v2 sau Sprint 2 |
| `SWP391_team_4.docx` | Sprint backlog, working plan, research proposal |

---

## 14. Quick Facts Cheat Sheet

```
URL backend:      http://localhost:5240
URL Supabase API: http://localhost:8000  (Kong → GoTrue + PostgREST + Studio + Storage)
URL Postgres:     localhost:5432  (direct, Supavisor pooler skip Phase 1)
Stack name:       aistudyhub-supabase  (compose project)
DB name:          postgres
DB user/pass:     postgres / <từ infra/supabase/.env POSTGRES_PASSWORD>
Containers:       Phase 1: supabase-db, supabase-kong, supabase-auth, supabase-rest,
                           supabase-meta, supabase-studio, supabase-analytics
                  Phase 2 (--profile phase2): + supabase-storage, supabase-vector,
                           supabase-realtime, supabase-imgproxy
                  Total expected: 11/11 healthy
Project root:     D:\FPT\summer2026\SWP391\AI_Study_Hub_v2
Solution:         AI_Study_Hub_v2.sln
Infra root:       D:\FPT\summer2026\SWP391\infra\supabase
Default admin:    admin@aistudyhub.local  (pwd ở password manager + dotnet user-secrets Seed:DefaultAdmin:Password)
Studio login:     supabase / <DASHBOARD_PASSWORD từ .env>
Roles seeded:     Admin, Student
Migrations:       20260524090408_InitialSupabaseAuth
                  20260525143314_AddDocumentSchema
Storage bucket:   documents (private, 50MB, 5 MIME: pdf/docx/pptx/doc/ppt — verified live 2026-05-26T09:43Z, matches DocumentService.AllowedMimeTypes 4-way)
.NET SDK:         10.0.300 (project targets net8.0)
EF tool:          9.0.9 (works against net8.0 project)
Docker:           29.4.3
Tests:            110/111 pass + 1 documented skip (combined workspace, final verified 2026-05-26T11:58Z)
                  D6 adds 18 folder/move tests; parallel RAG adds 5 tests
Build:            0 warning, 0 error (last verified 2026-05-26T11:58Z)
E2E smoke:        D6 document+folder flow PASS 2026-05-26T12:20Z; backend stopped after smoke
Worktree:         D6 code committed; remaining uncommitted = parallel AI/RAG + Home/Dashboard + live logs
Git HEAD:         2a0c5d5  feat(documents): D6 folder picker and move-to-folder flow
                  e03423e  docs(session): close 14 - D5 backend tests + Resume Pack refresh + F1 MIME drift fix
                  9dce4a0  test(documents): D5 backend tests for DocumentService + DocumentsController (SCRUM-28)
                  679b0d4  docs(session): close 13 - D4 list/detail UI code-complete
                  50a8122  feat(documents): D4 list/detail/delete UI (SCRUM-15/25) — code-complete
                  568177c  docs(session): close 12 - D2 smoke green + D3 upload form code-complete
                  8454b0d  feat(documents): D3 Blazor upload form (SCRUM-12/26) — code-complete
                  0245045  test(documents): D2 smoke E2E pass — upload/list/get/signed-download/delete
                  0e4340c  feat(documents): D2 backend upload pipeline — code-complete
                  c2d36cb  feat(documents): add Phase 2 schema (folders, documents, document_chunks)
```

---

**End of Resume Pack.** Cập nhật file này sau mỗi phase / sprint hoàn tất (Section 4 + Section 3.3 nếu schema đổi + Section 14 git head).
