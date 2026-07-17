# infra/supabase — Phase 1 stack

Local Supabase Auth + DB for `AI_Study_Hub_v2`. **Dev only.** Do not deploy this `.env` to production.

## Stack composition

**Default profile (Phase 1, ~1.3 GB RAM):**
- `db` — Postgres 15.8 + pgvector built-in (host port `5432`)
- `auth` — GoTrue v2.186 (via Kong `/auth/v1`)
- `kong` — API gateway, host port `8000`
- `rest` — PostgREST v14.8 (via Kong `/rest/v1`, optional for Phase 1)
- `meta` — postgres-meta (used by Studio)
- `studio` — Web UI at <http://localhost:8000>
- `analytics` — Logflare (Studio dependency)

**Phase 2 profile** (disabled, run with `docker compose --profile phase2 up -d`):
- `storage`, `imgproxy`, `realtime`, `functions` (edge runtime), `vector` (log shipper), `supavisor` (pooler)

## Quick start

```powershell
cd D:\FPT\summer2026\SWP391\infra\supabase
docker compose pull
docker compose up -d
docker compose ps
```

Open Studio: <http://localhost:8000> (login with `DASHBOARD_USERNAME` / `DASHBOARD_PASSWORD` from `.env`).

## Connection details for `AI_Study_Hub_v2`

```
ConnectionStrings:Postgres = Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=<POSTGRES_PASSWORD>
Supabase:Url               = http://localhost:8000
Supabase:JwtIssuer         = http://localhost:8000/auth/v1
Supabase:JwtAudience       = authenticated
Supabase:JwtSecret         = <JWT_SECRET from .env>
Supabase:AnonKey           = <ANON_KEY from .env>
Supabase:ServiceRoleKey    = <SERVICE_ROLE_KEY from .env>
```

User Secrets only — never commit.

## Health checks

```powershell
# GoTrue
curl http://localhost:8000/auth/v1/health
# DB + pgvector
docker exec -it supabase-db psql -U postgres -c "CREATE EXTENSION IF NOT EXISTS vector; SELECT '[1,2,3]'::vector;"
```

## Stop

```powershell
docker compose stop          # keep data
docker compose down          # remove containers, keep volumes
docker compose down -v       # destroy data — careful
```

## Regenerate secrets

If `.env` leaks or you need a fresh secret:

```powershell
# Get-Help Set-PSDebug, then run the same generator OpenCode used (see git history of this repo).
# After changing JWT_SECRET, also regenerate ANON_KEY and SERVICE_ROLE_KEY (they're HS256 JWTs signed with that secret).
```

Note: `volumes/db/data/` is bind-mounted — bumping `JWT_SECRET` requires `down -v` first or the DB-side `auth.users` JWTs become invalid for sessions but new logins keep working.

## Phase 1 deviations vs upstream

- Project name renamed `supabase` → `aistudyhub-supabase` to avoid collision with other Supabase stacks on this machine.
- 6 services tagged `profiles: ["phase2"]` (storage, imgproxy, realtime, functions, vector, supavisor).
- `db` exposes host port `5432:5432` directly (Supavisor disabled).
- `ENABLE_EMAIL_AUTOCONFIRM=true` (no SMTP setup needed for Phase 1).
- `JWT_EXPIRY=900` (15 min) to match prior Phase 1 behaviour.

## Registration policy

The self-hosted Compose file hard-sets `GOTRUE_DISABLE_SIGNUP=true`; the `.env` `DISABLE_SIGNUP` entry is informational/compatibility-only and cannot reopen public signup. Application self-registration is separately controlled by the database setting `auth.allow_self_registration`, which defaults to `false`; enable it only through the authenticated system-settings flow when public registration is intended.

For hosted Supabase, disable **Allow new users to sign up** before release. Release smoke must verify: an anon-key direct `/auth/v1/signup` request is rejected; service-role admin create succeeds; and password login plus refresh still succeed. Do not place service-role credentials in scripts or documentation.
