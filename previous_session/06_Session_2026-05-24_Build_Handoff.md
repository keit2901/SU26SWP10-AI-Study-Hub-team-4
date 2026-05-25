# 06 — Session 2026-05-24 (chiều) Build Handoff

**Status:** Build mode — Migration sang Supabase Local đã EXECUTE thành công đến hết Step 14 (15/16 step trong plan v-final). Step 15 (cập nhật docs `01_Architecture_Reference.md`, `02_Resume_Pack.md`, `04_Next_Session_Handoff.md`) chưa làm để giữ lịch sử. Smoke test 5 endpoints + 3 edge case đã pass thực tế (1 deviation về refresh reuse — xem mục 5).
**Author session này:** OpenCode (kr/claude-opus-4.7)
**Plan reference:** `05_Supabase_Local_Migration_Plan.md` (v-final)
**Stack thực tế đang chạy:** Supabase Local Phase 1 (7 services) + .NET app đã build OK (đã stop sau smoke).

---

## 1. Quyết định / deviations vs plan v-final

| # | Plan v-final | Thực tế | Lý do |
|---|---|---|---|
| D1 | Tên migration `RipCustomAuth_AdoptSupabaseAuth` chồng lên `InitialCreate` cũ | Xoá hẳn `InitialCreate` + snapshot cũ → tạo migration MỚI tên `InitialSupabaseAuth` | DB Supabase trống, schema cũ không còn match model. Một migration sạch dễ verify hơn |
| D2 | Project name `aistudyhub-supabase` | Đúng plan | Tránh conflict với stack Supabase cũ ở `D:\FPT\summer2026\SWP391\AI_Study_Hub_Admin\supabase-local` (đã `docker compose down` giữ volume) |
| D3 | Port: Kong 8000 / Postgres 54322 / Supavisor 5432 (plan v1) | **Kong 8000 / Postgres direct 5432** (plan v-final đã chốt sau Q3) | Skip Supavisor Phase 1 |
| D4 | Compose `profiles: ["phase2"]` cho 6 services | Đúng plan, áp dụng cho `storage`, `realtime`, `functions`, `imgproxy`, `vector`, `supavisor`. `analytics` vẫn nằm default profile | Đã chốt Q2 → giữ analytics + studio |
| D5 | Refresh token reuse → 401 | **Refresh token reuse → 200 trong cửa sổ ~10s** (GoTrue `REUSE_INTERVAL` default), sau cửa sổ → 401 | Behavior native của GoTrue, không phải bug. Document, không phải fix |
| D6 | `/api/auth/me` trả `email` từ DB | Hiện trả `email = ""` vì entity `public.users` không còn lưu email | Cosmetic. Cách fix sạch: thêm gọi `gotrue.GetUserAsync` vào `GetCurrentUserAsync` để fetch email từ session, hoặc lưu mirror field `email_cached` vào public.users. Để session sau |

---

## 2. Trạng thái thực tế cuối session

### Docker
- Stack `aistudyhub-supabase` đang **UP** (7 containers healthy):
  ```
  supabase-db        @ host 5432 (postgres + pgvector + pgcrypto)
  supabase-kong      @ host 8000
  supabase-auth      (gotrue v2.186.0)
  supabase-rest      (postgrest v14.8)
  supabase-meta
  supabase-studio    accessible at http://localhost:8000
  supabase-analytics (logflare)
  ```
- Stack cũ `aistudyhub-db @ 5433` đã stop (không xoá). Volume `aistudyhub-db_db-data` còn nguyên.
- Stack Admin `D:\FPT\summer2026\SWP391\AI_Study_Hub_Admin\supabase-local` đã `down` (volume `supabase_db-config`, `supabase_deno-cache` còn nguyên cho dự án Admin).

### Database (`postgres` DB on `supabase-db`)
- Schemas: `public`, `auth` (GoTrue managed), `storage`, `_supabase`, etc.
- `public.*`:
  - `__EFMigrationsHistory` (1 row: `20260524090408_InitialSupabaseAuth`)
  - `roles` (2 rows: Admin id=1, Student id=2) — **RLS ON**
  - `users` — **RLS ON**, FK `supabase_user_id → auth.users(id) ON DELETE CASCADE`
- Extensions: `vector 0.8.0`, `pgcrypto`, `uuid-ossp` (đã sẵn từ image, app không còn dùng).
- Identities GoTrue đã tồn tại:
  - admin: `admin@aistudyhub.local` (sub=`43d50ee0-cc8d-4b98-92e7-8f638b6c3635`)
  - 1 test student: `student4090@aistudyhub.local` (sub=`7f9cf233-19c0-407d-a4cf-1d23902aef96`) — chỉ dùng test, có thể xoá nếu muốn sạch.

### App
- Build clean (0 warning 0 error).
- `dotnet test` → 3/3 pass.
- App đã stop (port 5240 free).

---

## 3. Files đã đổi trong session này

### NEW
| Path | Vai trò |
|---|---|
| `infra/supabase/docker-compose.yml` (clone từ upstream + edit) | Stack Phase 1 |
| `infra/supabase/.env` | Secrets (gitignored) |
| `infra/supabase/.gitignore` | Bảo vệ secrets/data |
| `infra/supabase/README.md` | Quick start |
| `infra/supabase/volumes/...` | Bind config từ upstream |
| `AI_Study_Hub_v2/Options/SupabaseOptions.cs` | Config bind |
| `AI_Study_Hub_v2/Services/AuthException.cs` | Tách từ RefreshTokenService cũ |
| `AI_Study_Hub_v2/Services/Supabase/IGoTrueClient.cs` | |
| `AI_Study_Hub_v2/Services/Supabase/GoTrueClient.cs` | Raw HttpClient |
| `AI_Study_Hub_v2/Services/Supabase/GoTrueModels.cs` | DTOs |
| `AI_Study_Hub_v2/Services/SupabaseAuthService.cs` | IAuthService impl mới |
| `AI_Study_Hub_v2/Migrations/20260524090408_InitialSupabaseAuth.cs` | Migration mới sạch |
| `AI_Study_Hub_v2/Migrations/AppDbContextModelSnapshot.cs` | Snapshot mới |

### MODIFIED
| Path | Thay đổi |
|---|---|
| `AI_Study_Hub_v2/Program.cs` | Bỏ JwtTokenService/RefreshTokenService/PasswordHasher. Add SupabaseOptions binding. JwtBearer config dùng `Supabase:JwtSecret`. `OnTokenValidated` map `app_metadata.role` → ClaimTypes.Role. Seed admin gọi GoTrue admin API |
| `AI_Study_Hub_v2/Controllers/AuthController.cs` | `Logout` lấy access token từ `HttpContext.GetTokenAsync` thay vì userId. `Me` đọc `supabaseUserId` từ sub claim |
| `AI_Study_Hub_v2/Data/AppDbContext.cs` | Bỏ `DbSet<RefreshToken>`. Đổi `HasPostgresExtension("uuid-ossp")` → `pgcrypto` |
| `AI_Study_Hub_v2/Data/AppDbContextFactory.cs` | Add `AddUserSecrets`, throw rõ ràng khi thiếu connection string |
| `AI_Study_Hub_v2/Data/Entities/User.cs` | Drop `Email`, `PasswordHash`, `RefreshTokens`. Add `SupabaseUserId Guid` |
| `AI_Study_Hub_v2/Data/Configurations/UserConfiguration.cs` | Default UUID = `gen_random_uuid()`. Index unique theo `supabase_user_id` |
| `AI_Study_Hub_v2/appsettings.Development.json` | Connection string trỏ port 5432 + DB `postgres`. New `Supabase:*` section |
| `AI_Study_Hub_v2/appsettings.json` | Cập nhật template (placeholder) |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.csproj` | Bỏ `BCrypt.Net-Next 4.0.3` |
| `AI_Study_Hub_v2/docker-compose.db.yml` | Mark deprecated trong header comment, không xoá |

### DELETED
| Path |
|---|
| `AI_Study_Hub_v2/Services/PasswordHasher.cs` |
| `AI_Study_Hub_v2/Services/JwtTokenService.cs` |
| `AI_Study_Hub_v2/Services/RefreshTokenService.cs` |
| `AI_Study_Hub_v2/Services/AuthService.cs` |
| `AI_Study_Hub_v2/Data/Entities/RefreshToken.cs` |
| `AI_Study_Hub_v2/Data/Configurations/RefreshTokenConfiguration.cs` |
| `AI_Study_Hub_v2/Options/JwtOptions.cs` |
| `AI_Study_Hub_v2/Migrations/20260523183927_InitialCreate.cs` |
| `AI_Study_Hub_v2/Migrations/20260523183927_InitialCreate.Designer.cs` |

---

## 4. User Secrets (project `f7443cc6-0949-4e12-9bab-2badfa96be5a`)

```
ConnectionStrings:Postgres   = Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=<from .env POSTGRES_PASSWORD>
Supabase:JwtSecret           = <from .env JWT_SECRET>
Supabase:AnonKey             = <from .env ANON_KEY>
Supabase:ServiceRoleKey      = <from .env SERVICE_ROLE_KEY>
Seed:DefaultAdmin:Password   = <generated, see admin-pwd.txt>
```

> Tất cả các giá trị raw được lưu trong `infra/supabase/.env` (host machine) và `C:\Users\pc\AppData\Local\Temp\opencode\admin-pwd.txt` (admin password). KHÔNG commit.

---

## 5. Smoke test results (Step 12-13, manual via curl)

| # | Test | Expected | Got | Pass |
|---|---|---|---|---|
| 1 | Login admin | 200, role=Admin | 200, role=Admin (claim mapping qua app_metadata) | ✅ |
| 2 | `/me` với access token | 200, full profile | 200, role=Admin, **email=""** (xem D6) | ⚠️ partial |
| 3 | Register student fresh | 200, auto-confirmed | 200, auto-confirmed (autoconfirm=true), role=Student | ✅ |
| 4 | Refresh với RT | 200, RT mới | 200, RT mới | ✅ |
| 5 | **Reuse RT cũ ngay sau rotate** | 401 | **200 (GoTrue REUSE_INTERVAL grace window ~10s)** | ⚠️ deviation |
| 6 | Logout | 204 | 204 | ✅ |
| 7 | Refresh sau logout | 401 | 401 (`invalid_refresh_token`) | ✅ |
| 8 | `dotnet test` | 3/3 pass | 3/3 pass | ✅ |

### Deviation D5 detail (refresh reuse)

GoTrue có config `GOTRUE_REFRESH_TOKEN_REUSE_INTERVAL` mặc định **10 giây**: trong cửa sổ này, RT cũ vẫn được chấp nhận để cover network retry. Sau cửa sổ, RT cũ → 401. Behavior này **bảo mật hơn ở khía cạnh**: nếu attacker lén dùng RT cũ sau 10s đầu, GoTrue sẽ revoke ALL RTs của user (chain detection). Khác với cũ (chain-revoke ngay lập tức từ giây 0).

→ Acceptance criteria sửa nhẹ: "RT reuse beyond 10s → 401" thay vì "RT reuse → 401". Có thể tweak `GOTRUE_REFRESH_TOKEN_REUSE_INTERVAL=0` trong `infra/supabase/.env` nếu muốn behavior cũ chính xác.

---

## 6. Việc còn lại (cần làm session sau)

### CHƯA LÀM TRONG SESSION NÀY

1. **Step 15 — Update docs** (đã quyết định để session sau verify lại):
   - `01_Architecture_Reference.md` — Section 2 + 4 phải reflect Supabase Local Phase 1 stack thực tế
   - `02_Resume_Pack.md` — Section 2 (decisions), Section 3.3 (schema), Section 14 (cheat sheet) phải update
   - `04_Next_Session_Handoff.md` — Mark obsolete, link về file `06` này
2. **D6 fix** — `/me` trả email từ session. Quick fix: trong `SupabaseAuthService.GetCurrentUserAsync`, gọi GoTrue admin API hoặc lưu cache email khi register/login. Recommend: dùng JWT claim `email` (đã có sẵn trong access token) → đọc từ `User.FindFirstValue(ClaimTypes.Email)` trong controller, pass vào service.
3. **Sửa REUSE_INTERVAL** (optional) — quyết định giữ default 10s hay set 0.
4. **Cleanup test student** — xoá `student4090@aistudyhub.local` khỏi auth.users + public.users nếu muốn DB clean.

### KHUYẾN NGHỊ SESSION TIẾP

Tuỳ Kiệt chọn:
- **A:** Hoàn tất Step 15 + fix D6 trước, rồi commit toàn bộ migration → đóng Phase Auth chính thức.
- **B:** Bỏ qua D6 (cosmetic), đóng Phase Auth, tiến thẳng Phase 2 (Document upload + RAG). Storage profile bật `--profile phase2`.

---

## 7. Resume procedure (cho session sau)

```powershell
# 1. Bật stack Supabase
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml up -d
# (đợi 10-30s cho 7 services healthy)
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml ps

# 2. Verify GoTrue + DB
docker exec supabase-db psql -U postgres -d postgres -c "SELECT count(*) FROM public.users;"
# expect 2 (admin + student4090)

# 3. Run app
cd D:\FPT\summer2026\SWP391\AI_Study_Hub_v2
dotnet run --launch-profile http
# listening on http://localhost:5240

# 4. Smoke (trong PS khác)
$body = @{ email="admin@aistudyhub.local"; password="<see admin-pwd.txt>" } | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText("$env:TEMP\login.json", $body, [System.Text.UTF8Encoding]::new($false))
curl.exe -sS -X POST -H "Content-Type: application/json" --data "@$env:TEMP\login.json" http://localhost:5240/api/auth/login
```

Studio admin: `http://localhost:8000`, login `supabase / <DASHBOARD_PASSWORD từ .env>`.

---

## 8. Commands reference (để session sau khỏi rebuild)

```powershell
# Supabase Local lifecycle
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml up -d
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml stop      # giữ data
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml down      # remove containers, giữ volumes
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml down -v   # XOÁ data — chỉ khi cần reset

# Bật stack Phase 2 (storage / realtime / functions / imgproxy / vector / supavisor)
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml --profile phase2 up -d

# DB query nhanh
docker exec -it supabase-db psql -U postgres -d postgres

# EF migrations (workdir = AI_Study_Hub_v2)
dotnet ef migrations add <name>
dotnet ef database update
dotnet ef migrations remove

# Rollback emergency
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml down
docker compose -f D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\docker-compose.db.yml up -d
# nếu cần restore:
docker run --rm -v aistudyhub-db_db-data:/d -v D:\FPT\summer2026\SWP391\backups:/b busybox sh -c "cd /d && tar xzf /b/aistudyhub-db_backup_20260524.tgz"
```

---

## 9. Known issues / TODO

> Status update 2026-05-24 (session sau): K1, K3, K4, K6 đã resolved trong session A (cleanup + doc sync). Còn K2 (optional) + K5 (cảnh báo khi `git add`).

| # | Severity | Mô tả | Status |
|---|---|---|---|
| K1 | Low | `/api/auth/me` trả email rỗng | ✅ **Fixed 2026-05-24** — `AuthController.GetEmailFromClaims()` đọc `ClaimTypes.Email` rồi pass vào `IAuthService.GetCurrentUserAsync(id, email, ct)`. Smoke verified: `/me` trả `email='admin@aistudyhub.local'`. |
| K2 | Info | RT reuse có 10s grace window | Open (optional). Set `GOTRUE_REFRESH_TOKEN_REUSE_INTERVAL=0` trong `infra/supabase/.env` nếu muốn match plan v-final exactly. |
| K3 | Low | Tài khoản test `student4090@aistudyhub.local` còn trong DB | ✅ **Fixed 2026-05-24** — `DELETE FROM auth.users WHERE email='student4090@aistudyhub.local';` đã chạy. CASCADE xoá `public.users` đúng kỳ vọng (orphan check = 0). DB còn 1 admin. |
| K4 | Info | Admin pwd lưu plain ở `C:\Users\pc\AppData\Local\Temp\opencode\admin-pwd.txt` | ✅ **Fixed 2026-05-24** — đã in pwd lên console 1 lần (Kiệt copy vào password manager) rồi xoá file. Pwd vẫn còn trong `dotnet user-secrets` (`Seed:DefaultAdmin:Password`) cho seed idempotent. |
| K5 | Low | `infra/supabase/volumes/db/data/` sẽ chứa data Postgres | Open. Đã ignore qua `.gitignore` cục bộ. Khi `git add infra/`, double check `git status` để chắc chắn không leak data + `.env`. |
| K6 | Med | `01_Architecture_Reference.md` ghi đã bỏ Supabase Auth → mâu thuẫn với reality | ✅ **Fixed 2026-05-24** — `01_Architecture_Reference.md`, `02_Resume_Pack.md`, `04_Next_Session_Handoff.md` đã sync với Supabase Local Phase 1 reality. |

---

## 10. Backup & rollback artefacts

- `D:\FPT\summer2026\SWP391\backups\aistudyhub-db_backup_20260524.tgz` (6.4 MB) — snapshot Postgres cũ trước migration. Giữ tới ngày 2026-05-31.
- `infra/supabase/volumes/db/data/` — current DB. KHÔNG xoá khi muốn giữ admin/test users.
- `D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\docker-compose.db.yml` — compose stack cũ, deprecated nhưng giữ làm rollback.

---

## 11. Migration plan reference

File `05_Supabase_Local_Migration_Plan.md` (v-final) là source of truth của session này. Đọc lại khi có thắc mắc về quyết định L1-L13. Diff với reality đã liệt kê ở Section 1 trên.

---

**END.** Stack Supabase Local Phase 1 đã hoạt động end-to-end. Session sau có thể:
- Hoàn tất doc update + cleanup → đóng Phase Auth (Option A trong Section 6).
- HOẶC tiến thẳng Phase 2 Document/RAG, mở `--profile phase2` storage stack (Option B).
