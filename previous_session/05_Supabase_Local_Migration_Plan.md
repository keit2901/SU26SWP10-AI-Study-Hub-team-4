# 05 — Supabase Local Migration Plan (v-FINAL)

**Status:** v-FINAL (locked sau 13 câu hỏi của Kiệt — 8 câu Section 8 v1 + 5 câu clarify)
**Mode hiện tại:** BUILD (đã unlock từ PLAN). Chưa execute — chờ lệnh `GO BUILD` rõ ràng.
**Ngày soạn v1:** 2026-05-24 sáng
**Ngày update v-final:** 2026-05-24 chiều
**Author:** OpenCode (Claude Opus 4.7)
**Reference:** `04_Next_Session_Handoff.md` Section 4-5 + transcript Q&A trong session này

---

## 0. Mục tiêu

Rip toàn bộ Custom JWT / BCrypt / RefreshTokens. Thay bằng **Supabase Auth Local self-hosted (GoTrue)** trong Docker. Giữ contract endpoint `/api/auth/{register,login,refresh,logout,me}`.

**Out of scope:**
- Phase 2 Document/RAG/pgvector implementation (chỉ verify pgvector available)
- Phase 3 Citation (đã bỏ theo quyết định A3)
- Email confirmation, password reset

---

## 1. Locked Decisions (Final)

| # | Decision | Source |
|---|----------|--------|
| L1 | Auth backend = Supabase Local GoTrue | A1 handoff |
| L2 | Stack Phase 1 enabled: `db` + `auth` (gotrue) + `kong` + `rest` (postgrest) + `meta` (pg-meta) + `studio` + `analytics` (Logflare) | Q1, Q2 |
| L3 | Stack Phase 1 disabled qua compose `profiles: ["phase2"]`: `storage`, `realtime`, `edge-runtime` (functions), `imgproxy`, `vector` (log shipper Timberio), `supavisor` (pooler) | Q1, Q3, Q4 |
| L4 | pgvector extension built-in trong `supabase/postgres:15.x` — `CREATE EXTENSION vector;` chạy trong migration để sẵn sàng cho Phase 2 | Q1 |
| L5 | Postgres connection: **direct port 5432 → 5432** (skip Supavisor). EF migrations + app runtime cùng port | Q3 |
| L6 | Kong API gateway: 8000 → 8000 | Q2 |
| L7 | Admin email: `admin@aistudyhub.local` | Q3 v1 |
| L8 | GoTrue: `GOTRUE_MAILER_AUTOCONFIRM=true` (toàn cục, mọi user). Disable email confirmation Phase 1 | Q4 v1 |
| L9 | .NET package: chỉ raw `HttpClient` cho GoTrue. KHÔNG add `supabase-csharp` Phase 1 | Q5 v1 |
| L10 | RLS bare bones: `ENABLE ROW LEVEL SECURITY` trên `public.users` + `public.roles`. Service role bypass mặc định. Policy chi tiết để Phase 4 | Q6 v1 |
| L11 | Backup volume cũ: `D:\FPT\summer2026\SWP391\backups\aistudyhub-db_backup_<yyyyMMdd>.tgz` | Q7 v1 |
| L12 | Smoke test Phase 1: manual qua Postman + curl. Auto script Phase 2 | Q8 v1 |
| L13 | Disable mechanism: Compose `profiles`. `docker compose up -d` mặc định không start service Phase 2. Phase 2 dùng `docker compose --profile phase2 up -d` | Q4 |

---

## 2. Architectural Diff

### Trước (Phase 1 hiện tại)
```
Blazor → AuthApiClient → AuthController → AuthService
  → JwtTokenService (HS256) + RefreshTokenService (DB rotation)
  → AppDbContext (Postgres pgvector/pgvector:pg15 @ 5433)
Tables: roles, users(password_hash, email), refresh_tokens
```

### Sau (Supabase Local v-final)
```
Docker stack:
  kong (8000)
   ├─ /auth/v1/* → gotrue
   └─ /rest/v1/* → postgrest (Phase 2+ optional, app .NET ko bắt buộc dùng)
  db: supabase/postgres:15.x (pgvector built-in, port 5432→5432)
  meta + studio (UI admin), analytics (cho Studio dep)

Blazor → AuthApiClient → AuthController → SupabaseAuthService
  → IGoTrueClient (raw HttpClient) → http://localhost:8000/auth/v1/*
  → AppDbContext (cùng DB Postgres, schema public, FK sang auth.users)
Tables: roles, users(supabase_user_id FK auth.users(id))
KHÔNG còn refresh_tokens (GoTrue tự quản trong schema auth)
```

ASP.NET vẫn dùng `JwtBearer` middleware, đổi:
- `IssuerSigningKey` ← `Supabase:JwtSecret` (chính là `JWT_SECRET` từ `.env`)
- `ValidIssuer` ← `http://localhost:8000/auth/v1` (hoặc `http://kong:8000/auth/v1` nếu dùng container hostname)
- `ValidAudience` ← `authenticated`

---

## 3. Supabase Local Stack — Pinning + Compose

### 3.1 Approach
Clone `supabase/supabase`, copy `docker/` vào `infra/supabase/` của workspace (KHÔNG lồng vào `AI_Study_Hub_v2/`).

### 3.2 Files mới
| Path | Vai trò |
|---|---|
| `infra/supabase/docker-compose.yml` | Copy upstream + edit theo L2/L3/L5 |
| `infra/supabase/.env` | Local secrets (gitignored) |
| `infra/supabase/.env.example` | Commit, placeholders |
| `infra/supabase/volumes/` | Bind config (api/kong.yml, db/init/) |
| `infra/supabase/README.md` | Quick start + port map + how to regenerate keys |
| `.gitignore` patch | `infra/supabase/.env`, `infra/supabase/volumes/db/data/` |

### 3.3 Compose profile strategy (L13)
Trong `infra/supabase/docker-compose.yml`, **chỉ chỉnh các service Phase 2** thêm:
```yaml
storage:
  profiles: ["phase2"]
realtime:
  profiles: ["phase2"]
functions:        # edge-runtime
  profiles: ["phase2"]
imgproxy:
  profiles: ["phase2"]
vector:           # log shipper, không phải pgvector
  profiles: ["phase2"]
supavisor:        # connection pooler — Phase 2 mới enable nếu cần
  profiles: ["phase2"]
```

`docker compose up -d` mặc định **chỉ start** services không có profile = default profile = Phase 1 set của ta.

**Cần verify khi execute:** Studio + Analytics + Kong upstream KHÔNG có dependency cứng vào storage/realtime/functions. Section 5 step 3 sẽ check.

### 3.4 Port map v-final
| Service | Container | Host | Lý do |
|---|---|---|---|
| Kong (HTTP) | 8000 | **8000** | API gateway cho `/auth/v1`, `/rest/v1` |
| Kong (HTTPS) | 8443 | 8443 | Optional, không dùng Phase 1 |
| Postgres (db) | 5432 | **5432** | Direct connection, EF + app dùng chung |
| Studio | 3000 (qua Kong) | qua `http://localhost:8000` | Built-in routing |
| Analytics (Logflare) | 4000 | internal only | Studio dependency |

> Postgres cũ `aistudyhub-db @ 5433` shutdown sau migration. Volume giữ 7 ngày làm rollback.

### 3.5 Image tags pin
Chốt khi execute (Section 5 step 2). Snapshot upstream `2026-04-27`:
```
supabase/studio:2026.04.27-sha-5f60601
kong:3.9.1
supabase/gotrue:<latest tag stable, pin sau khi pull>
postgrest/postgrest:<v12.x stable>
supabase/postgres:15.8.x   (pgvector 0.8+ built-in)
supabase/postgres-meta:<latest stable>
supabase/logflare:<latest stable>
```
Action: `docker compose pull` → `docker compose images > images.snapshot.txt` → commit snapshot file vào repo.

### 3.6 Resource budget v-final
| Service | RAM ước tính |
|---|---|
| db | ~500 MB |
| auth (gotrue) | ~80 MB |
| kong | ~150 MB |
| rest (postgrest) | ~50 MB |
| meta | ~80 MB |
| studio | ~250 MB |
| analytics (logflare) | ~200 MB |
| **TỔNG Phase 1** | **~1.3 GB** |

So với 4 GB minimum của full stack → tiết kiệm ~70%. OK với máy 8 GB.

---

## 4. DB Schema Migration

### 4.1 Migration name
`20260525_RipCustomAuth_AdoptSupabaseAuth`

### 4.2 Up()
```sql
-- 1. Drop refresh_tokens (GoTrue tự quản trong schema auth)
DROP TABLE IF EXISTS public.refresh_tokens CASCADE;

-- 2. Migrate users
DROP INDEX IF EXISTS public."IX_users_email";
ALTER TABLE public.users
  DROP COLUMN email,
  DROP COLUMN password_hash,
  ADD COLUMN supabase_user_id UUID NOT NULL UNIQUE
    REFERENCES auth.users(id) ON DELETE CASCADE;

CREATE INDEX "IX_users_supabase_user_id" ON public.users(supabase_user_id);

-- 3. Enable pgvector cho Phase 2
CREATE EXTENSION IF NOT EXISTS vector;

-- 4. RLS bare bones (L10)
ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.roles ENABLE ROW LEVEL SECURITY;
-- Service role bypass mặc định. Không thêm policy Phase 1.
```

### 4.3 Down()
Recreate `email`, `password_hash`, `refresh_tokens`. Best-effort, không guarantee data restore.

### 4.4 DB connection
App + EF dùng cùng connection string:
```
Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=<POSTGRES_PASSWORD từ .env>
```
**Note:** DB name mặc định trong Supabase = `postgres` (không phải `aistudyhub`). Schema mới của ta nằm trong `public.*` của DB `postgres`. Không cần tạo DB riêng.

### 4.5 Mapping User entity
| Column trước | Sau | Note |
|---|---|---|
| `id UUID` | giữ | App-level profile PK (KHÁC `auth.users.id`) |
| `email` | **DROP** | Lấy từ `auth.users.email` qua join hoặc GoTrue API |
| `password_hash` | **DROP** | GoTrue lưu trong `auth.users.encrypted_password` |
| `username VARCHAR(15)` | giữ | App-specific |
| `full_name` | giữ | App-specific (cũng có thể lưu vào `auth.users.raw_user_meta_data`, nhưng giữ ở `public.users` để query thuận) |
| `role_id` | giữ | App-specific (Admin/Student) — KHÁC `auth.users.role` |
| `total_tokens_used` | giữ | Phase 2 |
| `is_active` | giữ | App-level disable |
| `created_at`, `updated_at` | giữ | |
| **`supabase_user_id UUID UNIQUE FK auth.users(id) ON DELETE CASCADE`** | NEW | Bridge |

---

## 5. .NET Project Changes

### 5.1 Packages
**Remove:** `BCrypt.Net-Next` 4.0.3
**Add Phase 1:** none (raw HttpClient)
**Add Phase 2 sau này:** `supabase-csharp` 1.1.x (cho Storage)

### 5.2 Files DELETE
| File |
|---|
| `Services/PasswordHasher.cs` |
| `Services/JwtTokenService.cs` |
| `Services/RefreshTokenService.cs` |
| `Data/Entities/RefreshToken.cs` |
| `Data/Configurations/RefreshTokenConfiguration.cs` |
| `Options/JwtOptions.cs` |

### 5.3 Files NEW
| File | Vai trò |
|---|---|
| `Options/SupabaseOptions.cs` | `Url, AnonKey, ServiceRoleKey, JwtSecret, JwtIssuer, JwtAudience` |
| `Services/Supabase/IGoTrueClient.cs` | Interface SignUp / SignInPassword / RefreshToken / SignOut / GetUser / AdminCreateUser |
| `Services/Supabase/GoTrueClient.cs` | Typed `HttpClient` → `{Url}/auth/v1/*` (+ apikey header = AnonKey, Authorization Bearer = ServiceRoleKey cho admin endpoints) |
| `Services/Supabase/GoTrueModels.cs` | DTOs: `GoTrueSession`, `GoTrueUser`, `GoTrueError` |
| `Services/SupabaseAuthService.cs` | `IAuthService` impl mới: orchestrate GoTrue + upsert profile `public.users` |
| `Migrations/20260525*_RipCustomAuth_AdoptSupabaseAuth.cs` | Section 4 |

### 5.4 Files MODIFY
| File | Thay đổi |
|---|---|
| `Program.cs` | Bỏ register PasswordHasher/JwtTokenService/RefreshTokenService. Bind `SupabaseOptions`. Add typed `HttpClient<GoTrueClient>`. JwtBearer config: `IssuerSigningKey` ← `Supabase:JwtSecret`, `ValidIssuer`, `ValidAudience` từ options. Seed admin: gọi GoTrue admin create user (auto-confirmed) → insert profile `public.users`. |
| `appsettings.Development.json` | `ConnectionStrings:Postgres` → port 5432 + DB `postgres`. New section `Supabase:{ Url, JwtIssuer, JwtAudience }`. Drop `Jwt:*`. |
| User Secrets (project scope) | Add `Supabase:JwtSecret`, `Supabase:AnonKey`, `Supabase:ServiceRoleKey`, `Seed:DefaultAdmin:Password`. Remove `Jwt:SigningKey`. |
| `Data/AppDbContext.cs` | Bỏ `DbSet<RefreshToken>`. Bỏ `HasPostgresExtension("uuid-ossp")` (Supabase image dùng `gen_random_uuid()` từ pgcrypto built-in). |
| `Data/Entities/User.cs` | Drop `Email`, `PasswordHash`. Add `SupabaseUserId Guid`. |
| `Data/Configurations/UserConfiguration.cs` | Update theo entity mới |
| `Dtos/AuthDtos.cs` | `UserDto.Email` lấy từ GoTrue session khi build response (không từ DB) |
| `Controllers/AuthController.cs` | Không đổi shape (dependency injection lấy `IAuthService` mới) |
| `Components/Pages/{Login,Register,Profile}.razor` | Không đổi (DTO shape giữ nguyên) |
| `docker-compose.db.yml` | Mark deprecated trong header comment, KHÔNG xóa file (giữ rollback) |

### 5.5 Endpoint behavior mapping
| Endpoint | Sau migration |
|---|---|
| `POST /api/auth/register` | `POST {gotrue}/auth/v1/signup` (auto-confirm vì L8) → upsert profile → return `AuthResponse` shape giống cũ |
| `POST /api/auth/login` | `POST {gotrue}/auth/v1/token?grant_type=password` |
| `POST /api/auth/refresh` | `POST {gotrue}/auth/v1/token?grant_type=refresh_token`. Reuse-detect do GoTrue handle. |
| `POST /api/auth/logout` | `POST {gotrue}/auth/v1/logout` (`scope=global` để revoke ALL sessions, khớp behavior cũ) |
| `GET /api/auth/me` | Read `public.users` join `auth.users` qua `supabase_user_id`, build `UserDto` |

### 5.6 Test infra
- Giữ `AI_Study_Hub_v2.Tests/SmokeTests.cs`
- Bonus: Phase 1 có thể thêm 1-2 unit test cho `SupabaseAuthService` mock `IGoTrueClient` (Moq sẵn rồi). Optional, không blocker.
- Integration test thật vs Supabase Local: để Phase 2 setup CI sau.

---

## 6. Execution Order (sau khi Kiệt nói "GO BUILD")

| # | Bước | Verify |
|---|------|--------|
| 0 | Tạo dir `D:\FPT\summer2026\SWP391\backups\` nếu chưa có | `Test-Path` true |
| 1 | Backup volume `aistudyhub-db_db-data` → `backups\aistudyhub-db_backup_20260524.tgz` | File tồn tại, size > 0 |
| 2 | Stop app .NET cũ (pid file) + stop container `aistudyhub-db` (KHÔNG `down -v`) | Port 5240 + 5433 không listen |
| 3 | Tạo `infra/supabase/` từ upstream (sparse checkout). Edit compose: thêm `profiles: ["phase2"]` cho 6 services Phase 2. Generate `.env`: random `POSTGRES_PASSWORD`, `JWT_SECRET` (32+ chars), gen `ANON_KEY` + `SERVICE_ROLE_KEY` từ `JWT_SECRET` qua HS256 sign | `docker compose config` không lỗi |
| 4 | `docker compose pull` → `docker compose up -d` (mặc định, không profile) | Stack 7 services healthy trong < 90s |
| 5 | Verify: `curl http://localhost:8000/auth/v1/health` 200; `psql -h localhost -p 5432 -U postgres -c "CREATE EXTENSION IF NOT EXISTS vector; SELECT '[1,2,3]'::vector;"` trả `[1,2,3]` | OK |
| 6 | Verify Studio: mở `http://localhost:8000` → login bằng dashboard creds → thấy schema `auth`, `public`, `storage` | UI load được |
| 7 | Code thay đổi Section 5.2-5.4. `dotnet build` sau mỗi nhóm file | 0 error 0 warning |
| 8 | `dotnet ef migrations add RipCustomAuth_AdoptSupabaseAuth --project AI_Study_Hub_v2.csproj` | Migration file generated, build pass |
| 9 | Update User Secrets: `dotnet user-secrets remove Jwt:SigningKey`, `dotnet user-secrets set Supabase:JwtSecret <value>` v.v. | `dotnet user-secrets list` ra đúng |
| 10 | `dotnet ef database update` (connect 5432) | Tables `users`, `roles` trong `public` schema; pgvector extension enabled |
| 11 | Run app DEV → seed admin chạy: GoTrue admin API create `admin@aistudyhub.local` (auto-confirmed) + insert profile | `auth.users` 1 row, `public.users` 1 row link đúng `supabase_user_id` |
| 12 | Manual smoke test 5 endpoints qua Postman/curl (Resume Pack Section 8 snippet, base URL không đổi) | All pass |
| 13 | Manual edge cases: reuse refresh sau rotate → 401; `/refresh` sau `/logout` → 401 | Pass |
| 14 | `dotnet test` | 3/3 smoke tests vẫn green |
| 15 | Update docs: `01_Architecture_Reference.md` Section 2+4, `02_Resume_Pack.md` Section 2-3-14, `04_Next_Session_Handoff.md` mark obsolete (link sang `05_v-final`) | Files updated, chưa commit (chờ Kiệt) |

---

## 7. Risks + Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Studio depends on `analytics` healthy → start fail | High | Đã giữ `analytics` trong stack Phase 1 (L2) |
| Compose `profiles` không hoạt động đúng version Docker cũ | Low | Yêu cầu Docker Compose v2+ (Kiệt đã có Docker 29.4.3 → OK) |
| GoTrue claim format khác (`role` vs `app_metadata.role`) | Medium | Khi build, đọc raw JWT sau khi GoTrue cấp → adjust JwtBearer `OnTokenValidated` map claim nếu cần. Pre-emptive: dùng `app_metadata.role` cho app role |
| Kong port 8000 conflict với dev tool | Low | Đổi `KONG_HTTP_PORT` trong `.env` |
| Mất user data (admin + 3 test users) | Accepted | Không migrate data, seed lại admin. Test users tạo mới khi smoke |
| EF với DB tên `postgres` (vs `aistudyhub` cũ) gây nhầm | Low | Document rõ trong appsettings comment + cheat sheet Resume Pack |
| GoTrue auto-confirm có thể quên tắt khi production | Medium | TODO comment trong appsettings + ghi rõ "PHASE 1 ONLY" trong README |
| pgcrypto vs uuid-ossp khác nhau khi gen UUID | Low | Bỏ `HasPostgresExtension("uuid-ossp")` trong AppDbContext, đổi default `gen_random_uuid()` (pgcrypto sẵn có trong Supabase image) |
| Reuse detection của GoTrue (revoked flag) khác behavior cũ (chain-revoke aggressive) | Low | Document trong release note |

---

## 8. Acceptance Criteria

Migration DONE khi:

1. `docker compose up -d` ở `infra/supabase/` → 7 services healthy trong < 90s
2. `dotnet build` 0 warning 0 error
3. `dotnet ef database update` apply migration sạch, pgvector enabled
4. Smoke test 5 endpoints + 3 edge cases pass:
   - Login admin → 200, role=Admin
   - `/me` 200
   - Register Student auto-confirmed → 200, login được ngay
   - Refresh rotate → 200
   - Logout → 204 (revoke all)
   - Reuse old refresh → 401
   - Refresh sau logout → 401
5. Admin login qua Blazor `/login` → `/profile` → JWT viewer hiển thị `role: Admin` (qua claim mapping)
6. `dotnet test` 3/3 green
7. Postgres cũ `aistudyhub-db` stopped, volume chưa xóa (rollback window 7 ngày)
8. Docs updated: `01_Architecture_Reference.md`, `02_Resume_Pack.md`, `04_Next_Session_Handoff.md`

---

## 9. Rollback Plan

Nếu fail giữa chừng:
1. `docker compose -f infra/supabase/docker-compose.yml down`
2. `docker compose -f AI_Study_Hub_v2/docker-compose.db.yml up -d`
3. `git restore .` (assume changes uncommitted) hoặc `git revert`
4. EF: nếu DB cũ vẫn còn schema cũ thì không cần làm gì. Nếu lỡ apply migration vào DB cũ (không nên xảy ra) → `dotnet ef database update <PreviousMigration>`
5. Smoke test 5 endpoints cũ → confirm rollback OK
6. Restore từ tgz nếu cần: `docker run --rm -v aistudyhub-db_db-data:/d -v <backups>:/b busybox sh -c "cd /d && tar xzf /b/aistudyhub-db_backup_20260524.tgz --strip 1"`

Window: 7 ngày, sau đó `docker volume rm aistudyhub-db_db-data` để giải phóng disk.

---

## 10. Estimate v-final

| Pha | Effort |
|---|---|
| Stack setup + verify (step 0-6) | 60-90 phút |
| Code changes + build (step 7-9) | 90-120 phút |
| Migration + seed + smoke (step 10-13) | 60 phút |
| Docs (step 14-15) | 30 phút |
| **Total** | **~5 giờ** |

Buffer 50% cho lần đầu deploy stack lạ → kế hoạch 1 ngày làm việc (8h).

---

## 11. v1 → v-final Changelog

| Section | Thay đổi |
|---|---|
| 1 (Locked decisions) | Mới — consolidate 13 quyết định |
| 3.3 (Profile strategy) | Mới — dùng compose `profiles` thay vì comment-out |
| 3.4 (Port map) | Bỏ Supavisor 5432 + direct 54322. Còn duy nhất Postgres 5432 direct |
| 3.6 (Resource budget) | Detail RAM per service ~1.3GB total |
| 4.4 (DB connection) | DB name = `postgres` (Supabase default), không phải `aistudyhub` |
| 5.1 (Packages) | Confirm KHÔNG add `supabase-csharp` Phase 1 |
| 6 (Execution) | 16 bước (0-15), thêm step 0 (tạo backup dir), step 6 (verify Studio UI) |
| 7 (Risks) | Thêm risks về analytics dependency, compose profiles version, DB name nhầm, auto-confirm forget |
| 9 (Rollback) | Thêm step restore từ tgz |

---

**END v-FINAL.** Sẵn sàng execute Section 6 sau khi Kiệt confirm "GO BUILD" rõ ràng.
