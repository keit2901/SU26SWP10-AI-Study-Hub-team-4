# AI Study Hub v2 — Quick Start

Setup từ 0 → app chạy được trong **~5-10 phút**. Script `setup.ps1` lo:

- Generate Supabase secrets (JWT, anon/service-role keys, Postgres password, dashboard password)
- Ghi `infra/supabase/.env`
- `docker compose up -d` Supabase Local stack (7 containers)
- Wait Postgres healthy
- `dotnet user-secrets set` 5 keys cho app
- `dotnet build` verify

---

## 1. Yêu cầu môi trường

| Tool | Version | Verify |
|---|---|---|
| Docker Desktop (Win/Mac) hoặc Docker Engine + Compose v2 (Linux) | Compose v2.x | `docker compose version` |
| .NET SDK | 8.0.x trở lên | `dotnet --version` |
| PowerShell | 5.1 (Win sẵn) hoặc PowerShell 7+ | `$PSVersionTable.PSVersion` |
| Disk free | ~3 GB cho Docker images + Postgres data | |
| Port free | 5432, 8000, 8443, 4000, 5240 | `Get-NetTCPConnection -LocalPort 5432,8000,8443,4000,5240` |

Nếu chưa có Docker / .NET 8 — cài rồi quay lại.

---

## 2. Setup (1 lệnh)

Mở PowerShell **ở thư mục SWP391 root** (chứa `setup.ps1`, `AI_Study_Hub_v2/`, `infra/`):

```powershell
.\setup.ps1
```

**Output kỳ vọng:**

```
==> Checking prerequisites...
OK   docker OK, dotnet 8.0.xxx
==> Generating fresh Supabase secrets...
OK   Wrote D:\...\infra\supabase\.env
==> Starting Supabase Local stack (docker compose up -d)...
==> Waiting for supabase-db to become healthy (max 120s)...
OK   supabase-db healthy.
==> Configuring dotnet user-secrets...
OK   user-secrets set (5 keys).
==> Running dotnet build...
OK   Build succeeded.

=========================================================
  SETUP COMPLETE
=========================================================
  Postgres        : localhost:5432  (password in user-secrets)
  Kong gateway    : http://localhost:8000  (Studio + GoTrue + REST)
  Studio login    : supabase / <random>
  App will run at : http://localhost:5240

  Default admin email: admin@aistudyhub.local
  Default admin pwd  : <copy ngay vao password manager>
```

> **Quan trọng:** copy `Default admin pwd` ngay khi script in ra. Pwd cũng nằm trong `dotnet user-secrets` (key `Seed:DefaultAdmin:Password`) để app re-seed idempotent — nhưng tốt nhất lưu vào password manager riêng.

### Các flag thêm

```powershell
.\setup.ps1 -Force      # regenerate ALL secrets, overwrite .env (mất login admin cũ — DB cần reset)
.\setup.ps1 -SkipUp     # đã có stack chạy, chỉ cần set user-secrets từ .env hiện có
.\setup.ps1 -SkipBuild  # bỏ qua dotnet build cuối
```

---

## 3. Chạy app

```powershell
cd AI_Study_Hub_v2
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240
```

Lần đầu chạy: app sẽ
1. Apply EF migrations vào `public.*` (tạo `users`, `roles`, `__EFMigrationsHistory`)
2. Gọi GoTrue admin API tạo `auth.users` cho admin
3. Insert profile mirror vào `public.users` với role=Admin

Sau đó:
- UI: `http://localhost:5240/login` — đăng nhập `admin@aistudyhub.local` + pwd từ setup
- API: `POST http://localhost:5240/api/auth/login`
- Studio dashboard: `http://localhost:8000` — login `supabase / <DASHBOARD_PASSWORD trong .env>`

---

## 4. Chạy tests (offline, không cần Docker)

Test suite hoàn toàn isolated — dùng EF InMemory + Moq, không đụng Postgres / GoTrue thật:

```powershell
dotnet test AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj
```

Expected: **38/38 pass** (~1 giây).

| Suite | Count | Coverage |
|---|---|---|
| `SmokeTests` | 3 | NUnit + FluentAssertions + project ref OK |
| `Services/SupabaseAuthServiceTests` | 18 | Register / Login / Refresh / Logout / Me — happy + error paths |
| `Controllers/AuthControllerTests` | 17 | Claim parsing (sub/email fallback), AuthException → status code, Bearer header |

---

## 5. Stop / Cleanup

```powershell
# Stop app: Ctrl+C trong terminal đang chạy dotnet run

# Stop stack (giữ data)
docker compose -f infra\supabase\docker-compose.yml stop

# Down (remove containers, giữ volumes — DB data còn nguyên)
docker compose -f infra\supabase\docker-compose.yml down

# DESTRUCTIVE — reset toàn bộ DB + storage volumes (mất login admin cũ)
docker compose -f infra\supabase\docker-compose.yml down -v
```

Sau khi `down -v`, lần `setup.ps1` sau (không `-Force`) sẽ reuse `.env` cũ + reuse pwd cũ trong user-secrets, nhưng DB sạch → seed lại admin từ đầu (idempotent path).

---

## 6. Troubleshooting

### Docker daemon not reachable

Mở Docker Desktop. Đợi icon system tray xanh.

### Port 5432 / 8000 / 5240 đang bị chiếm

```powershell
# Tìm process chiếm port
Get-NetTCPConnection -LocalPort 5432,8000,5240 | Select LocalPort, OwningProcess
Get-Process -Id <PID>
```

Postgres local có thể đang chạy trên 5432 — stop service `postgresql-x64-15` (Services.msc) hoặc đổi port `POSTGRES_PORT` trong `.env`.

### `setup.ps1` báo execution policy

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\setup.ps1
```

### App start báo `Supabase:JwtSecret is required and must be >= 32 characters`

User-secrets không set hoặc lệch project. Re-run:
```powershell
.\setup.ps1 -SkipUp -SkipBuild
```

### Login 401 invalid_credentials

Pwd lệch giữa GoTrue (`auth.users.encrypted_password`) và `Seed:DefaultAdmin:Password` trong user-secrets. Cách reset:
```powershell
docker compose -f infra\supabase\docker-compose.yml down -v
.\setup.ps1 -Force
cd AI_Study_Hub_v2; dotnet run --no-launch-profile --urls http://localhost:5240
```
→ DB sạch, admin được tạo lại với pwd mới in ra console.

### Stack `unhealthy` trong > 2 phút

```powershell
docker compose -f infra\supabase\docker-compose.yml ps
docker compose -f infra\supabase\docker-compose.yml logs supabase-db --tail 100
docker compose -f infra\supabase\docker-compose.yml logs supabase-auth --tail 100
```

Lỗi phổ biến: máy không đủ RAM (cần ~2 GB free cho stack) hoặc image pull bị slow.

---

## 7. Cấu trúc thư mục cần zip / share

Khi gửi project lên Drive cho người khác, **chỉ cần kèm**:

```
SWP391/
├── setup.ps1                ← bootstrap script
├── QUICK_START.md           ← file này
├── AI_Study_Hub_v2/         ← app code (loại trừ bin/, obj/)
└── infra/
    └── supabase/            ← Docker stack (loại trừ volumes/db/data/, volumes/storage/, .env)
```

**KHÔNG kèm:**
- `infra/supabase/volumes/db/data/` — Postgres binary data, không portable, dễ corrupt khi zip lúc đang chạy
- `infra/supabase/volumes/storage/` — Phase 2 storage data
- `infra/supabase/.env` — chứa secrets, người nhận chạy `setup.ps1` để gen mới
- `AI_Study_Hub_v2/bin/`, `obj/` — build output, sẽ được tạo lại khi `dotnet build`
- `backups/` — backup riêng máy bạn
- `previous_session/` — trừ khi muốn người nhận có context handoff đầy đủ

`.gitignore` ở `infra/supabase/` đã exclude sẵn `.env` + `volumes/db/data/`. Nếu zip thì thêm filter:

```powershell
# PowerShell: tạo zip sạch để share
Compress-Archive -Path setup.ps1, QUICK_START.md, AI_Study_Hub_v2, infra `
    -DestinationPath SWP391_share.zip `
    -Force
# Sau đó xoá artifacts không cần trong zip — hoặc dùng 7-Zip với exclusion list:
#   "-xr!bin" "-xr!obj" "-xr!volumes\db\data" "-xr!volumes\storage" "-xr!.env"
```

---

## 8. Phase status

- **Phase 1 Auth (Supabase GoTrue):** ✅ DONE 2026-05-24. 5 endpoints (`register`, `login`, `refresh`, `logout`, `me`). 38/38 tests pass.
- **Phase 2 (Document upload + chunking + embeddings + RAG):** 📋 Plan v1 ở `previous_session/07_Phase2_Document_RAG_Plan.md` — chờ Q1-Q10 confirm trước GO BUILD.

Detail handoff context: `previous_session/02_Resume_Pack.md` + `previous_session/08_Session_2026-05-24_Close_Phase1_Handoff.md`.
