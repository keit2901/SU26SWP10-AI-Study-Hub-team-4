# AI Study Hub v2 — Resume Pack

> **Mục đích:** Mở session mới với agent (Claude / OpenCode / Kiro / khác) → paste/đính kèm file này làm context đầu tiên → agent có đủ thông tin để **không hỏi lại** và **không phá tiến độ**.
> **Cập nhật lần cuối:** 2026-05-24 (sau migration sang Supabase Local Phase 1 + fix D6 `/me` email)
> **Người maintain:** Kiệt — PM Team 4 SWP391 SU26
> **Phase hoàn tất:** Auth Phase 1 — đã migrate sang Supabase Local GoTrue, ready cho Phase 2 (Document Management + RAG)
> **File companion:** `06_Session_2026-05-24_Build_Handoff.md` (chi tiết session migration), `05_Supabase_Local_Migration_Plan.md` (plan v-final)

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
│                                     JwtBearer 8.0.10, MudBlazor 9.4.0
│                                     (BỎ BCrypt.Net-Next sau migration)
├── AI_Study_Hub_v2.sln
├── Program.cs                       ← DI, JwtBearer (validate GoTrue token), EF migrate-on-startup,
│                                     OnTokenValidated map app_metadata.role → ClaimTypes.Role,
│                                     SeedDefaultAdminAsync (gọi GoTrue admin API + insert public.users)
├── appsettings.json                 ← prod skeleton (no secrets)
├── appsettings.Development.json     ← dev: connstr port 5432 DB=postgres,
│                                     Supabase:{Url, JwtIssuer, JwtAudience, JwtSecret(secret),
│                                     AnonKey(secret), ServiceRoleKey(secret)},
│                                     Seed:DefaultAdmin (no password — secret)
├── docker-compose.db.yml            ← DEPRECATED (giữ làm rollback), header comment cảnh báo
├── Properties/launchSettings.json
├── Components/                      ← (như cũ — Blazor pages chưa rework cho refresh persistence)
├── Controllers/
│   └── AuthController.cs            ← /api/auth/{register,login,refresh,logout,me}
│                                     Logout đọc access token từ HttpContext.GetTokenAsync,
│                                     Me đọc supabaseUserId từ sub claim + email từ ClaimTypes.Email
├── Data/
│   ├── AppDbContext.cs              ← bỏ DbSet<RefreshToken>, dùng pgcrypto thay uuid-ossp
│   ├── AppDbContextFactory.cs       ← AddUserSecrets, throw rõ khi thiếu connstr
│   ├── Entities/{User,Role}.cs      ← User: bỏ Email/PasswordHash/RefreshTokens,
│   │                                  add SupabaseUserId Guid + index unique
│   └── Configurations/{User,Role}Configuration.cs
├── Migrations/
│   ├── 20260524090408_InitialSupabaseAuth.cs    ← migration mới sạch
│   ├── 20260524090408_InitialSupabaseAuth.Designer.cs
│   └── AppDbContextModelSnapshot.cs
├── Dtos/AuthDtos.cs                 ← (như cũ — RegisterRequest, LoginRequest, RefreshTokenRequest,
│                                     AuthResponse, UserDto, ApiErrorResponse)
├── Options/
│   ├── SupabaseOptions.cs           ← Url, JwtIssuer, JwtAudience, JwtSecret, AnonKey, ServiceRoleKey
│   └── SeedOptions.cs               ← DefaultAdmin { Email, Username, FullName, Password }
│                                     (BỎ JwtOptions.cs sau migration)
├── Services/
│   ├── AuthException.cs             ← tách từ RefreshTokenService cũ
│   ├── Supabase/
│   │   ├── IGoTrueClient.cs         ← interface GoTrue HTTP wrapper
│   │   ├── GoTrueClient.cs          ← raw HttpClient (signup, signInWithPassword, refresh,
│   │   │                              signOut, admin getUser/createUser)
│   │   └── GoTrueModels.cs          ← DTOs cho GoTrue API
│   ├── SupabaseAuthService.cs       ← IAuthService impl mới (mirror profile vào public.users)
│   ├── AuthApiClient.cs             ← typed HttpClient cho Blazor pages
│   └── AuthSessionState.cs          ← scoped per-circuit holder (in-memory, demo-only)
└── wwwroot/                         ← (như cũ)

AI_Study_Hub_v2.Tests/
├── AI_Study_Hub_v2.Tests.csproj     ← NUnit 3.14 + FluentAssertions 6.12 + Moq 4.20 +
│                                     EF Core InMemory 8.0.10 + Mvc.Testing 8.0.10 + coverlet
├── SmokeTests.cs                    ← 3 sanity tests (pipeline OK, project ref compile)
├── Support/
│   └── TestDb.cs                    ← InMemory AppDbContext factory, pre-seed 2 roles
├── Services/
│   └── SupabaseAuthServiceTests.cs  ← 18 unit tests cover Register/Login/Refresh/Logout/Me
│                                     mock IGoTrueClient, EF InMemory cho public.users
└── Controllers/
    └── AuthControllerTests.cs       ← 17 tests cover claim parsing (sub/email fallback),
                                     AuthException → status code mapping, Bearer header,
                                     stub IAuthenticationService cho HttpContext.GetTokenAsync
```

**Files đã DELETE sau migration (không còn trên disk):**
`Services/PasswordHasher.cs`, `Services/JwtTokenService.cs`, `Services/RefreshTokenService.cs`, `Services/AuthService.cs`, `Data/Entities/RefreshToken.cs`, `Data/Configurations/RefreshTokenConfiguration.cs`, `Options/JwtOptions.cs`, migration cũ `20260523183927_InitialCreate.*`.

### 3.2 Build status

```
dotnet build (cwd: AI_Study_Hub_v2)
→ Build succeeded. 0 Warning(s). 0 Error(s).
dotnet test → Passed! 38/38 (Duration: ~870 ms)
            ├─ SmokeTests: 3 (pipeline sanity)
            ├─ SupabaseAuthServiceTests: 18 (Register/Login/Refresh/Logout/Me happy + error paths)
            └─ AuthControllerTests: 17 (claim parsing + AuthException mapping + Bearer header)
```

### 3.3 Database state (Postgres `postgres` DB on container `supabase-db` @ localhost:5432)

Schemas: `public`, `auth` (GoTrue), `storage`, `_supabase`, `extensions`, `realtime`, `pgsodium`, ...

Tables in `public`:
```
public | __EFMigrationsHistory | table | postgres
public | roles                 | table | postgres   -- RLS ON
public | users                 | table | postgres   -- RLS ON
```

Migrations applied: `20260524090408_InitialSupabaseAuth`

Seeded data:
- 2 roles: `Admin`, `Student` (`public.roles`)
- 1 admin: `admin@aistudyhub.local` (auth.users + public.users mirror, role=Admin)
- DB clean — test student `student4090@aistudyhub.local` đã xoá 2026-05-24 (K3 trong file 06).

Extensions enabled in `postgres` DB: `vector 0.8.0`, `pgcrypto`, `uuid-ossp` (sẵn từ image).

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
| 8 | Phase 2: Document upload + Supabase Storage + chunking + embeddings + RAG | ⏳ Pending |

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

Phase 1 chỉ có 2 bảng app trong `public.*` (profile mirror + role). Identity (password, refresh, session) sống trong `auth.*` do GoTrue quản lý — **app KHÔNG đụng trực tiếp**, mọi thao tác qua HTTP API. Schema 14-bảng đầy đủ là Phase 2+, **chưa add** vào model.

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

-- auth.* (GoTrue managed — đừng tạo migration đụng vào)
auth.users          -- id, email, encrypted_password, email_confirmed_at, raw_app_meta_data, raw_user_meta_data, ...
auth.identities     -- provider linkage
auth.refresh_tokens -- rotation + reuse detection (10s grace window)
auth.sessions       -- aal level, factor_id
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

## 12. Phase 2 — Backlog (chưa làm, để session sau lên plan chi tiết)

Sprint 2 scope:
- Document upload qua Blazor → **Supabase Storage** (bật `--profile phase2`)
- Chunking strategy (size, overlap, page metadata cho citation)
- Embeddings → lưu pgvector cột `chunks.embedding vector(N)`
- Vector search endpoint
- RAG pipeline cơ bản (retrieve → prompt → Groq Llama 3.1 free tier → response)
- Add Schema bảng `Folders, Documents, DocumentChunks` qua EF migration mới (đặt tên `AddDocumentSchema`)

Trước khi bắt đầu Phase 2, cần Kiệt confirm:
- Supabase Storage bucket: tên, public/private, size limit
- Groq API key đã có chưa, quota free tier
- Embedding model + N của `vector(N)` (Groq embed, sentence-transformers, ...)
- Chunking config (size 500/1000 tokens? overlap 10/20%?)

---

## 13. Files Liên Quan

| File | Vai trò |
|---|---|
| `previous_session/02_Resume_Pack.md` | **File này** — primary resume context, đọc mỗi session mới |
| `previous_session/01_Architecture_Reference.md` | Target schema + phase roadmap (đã refresh sau migration) |
| `previous_session/03_Prompt_Playbook.md` | Template prompt sẵn cho session mới |
| `previous_session/05_Supabase_Local_Migration_Plan.md` | Plan v-final của migration session |
| `previous_session/06_Session_2026-05-24_Build_Handoff.md` | **Build log session migration** — deviations, smoke test, known issues |
| `previous_session/04_Next_Session_Handoff.md` | OBSOLETE — viết trước migration, giữ làm history |
| `previous_session/archive/previous_session_raw_transcript.md` | Raw Q&A transcript session trước, debug step-by-step |
| `infra/supabase/docker-compose.yml` | Supabase Local stack (Phase 1 default + Phase 2 profile) |
| `infra/supabase/.env` | Secrets Supabase Local (gitignored) |
| `AI_Study_Hub_Project_Overview.md` | Overview cũ của nhóm, cần update v2 sau Sprint 2 |
| `SWP391_team_4.docx` | Sprint backlog, working plan, research proposal |

---

## 14. Quick Facts Cheat Sheet

```
URL backend:      http://localhost:5240
URL Supabase API: http://localhost:8000  (Kong → GoTrue + PostgREST + Studio)
URL Postgres:     localhost:5432  (direct, Supavisor pooler skip Phase 1)
Stack name:       aistudyhub-supabase  (compose project)
DB name:          postgres
DB user/pass:     postgres / <từ infra/supabase/.env POSTGRES_PASSWORD>
Containers:       supabase-db, supabase-kong, supabase-auth, supabase-rest,
                  supabase-meta, supabase-studio, supabase-analytics
Phase 2 profile:  --profile phase2  (storage, realtime, functions, imgproxy, vector, supavisor)
Project root:     D:\FPT\summer2026\SWP391\AI_Study_Hub_v2
Solution:         AI_Study_Hub_v2.sln
Infra root:       D:\FPT\summer2026\SWP391\infra\supabase
Default admin:    admin@aistudyhub.local  (pwd ở password manager + dotnet user-secrets Seed:DefaultAdmin:Password)
Studio login:     supabase / <DASHBOARD_PASSWORD từ .env>
Roles seeded:     Admin, Student
Migration ID:     20260524090408_InitialSupabaseAuth
.NET SDK:         10.0.300 (project targets net8.0)
EF tool:          9.0.9 (works against net8.0 project)
Docker:           29.4.3
```

---

**End of Resume Pack.** Cập nhật file này sau mỗi phase hoàn tất (Section 4 + Section 3.3 nếu schema đổi).
