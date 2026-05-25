# AI Study Hub — Architecture Reference (Long-Lived)

> **Mục đích:** Quyết định kiến trúc gốc + target schema + phase roadmap.
> **Đọc khi nào:** Lúc planning Phase 2+, hoặc khi cần lookup target schema.
> Cho state hiện tại + cách resume → đọc `02_Resume_Pack.md`.
> Cho chi tiết build session migration sang Supabase Local → đọc `06_Session_2026-05-24_Build_Handoff.md`.
> **Cập nhật:** 2026-05-24 (sau migration sang Supabase Local Phase 1)

---

## 1. Bối Cảnh

- **Project:** AI Study Hub — web app cho sinh viên FPT upload tài liệu, hỏi đáp qua RAG chatbot
- **Course:** SWP391 SU26, FPT University
- **Team:** Team 4 — Kiệt (PM), Long, Sơn, Phước, Bảo, Duy Anh
- **Deliverable cuối:** Demo end-to-end + research paper Sub-RQ 3 (citation accuracy)

---

## 2. Tech Stack (Locked)

| Layer | Tech |
|---|---|
| Frontend | Blazor 8 Interactive Server + MudBlazor 9.4 |
| Backend | ASP.NET Core 8 Web API (cùng project với Blazor) |
| Database | Supabase Local Postgres 15 + pgvector (image `supabase/postgres:15.x`) |
| Auth | **Supabase GoTrue** (self-hosted, JWT HS256) — domain role qua `app_metadata.role` |
| Storage | Supabase Storage (Phase 2 — bật qua `--profile phase2`) |
| AI | Groq API (Llama 3.1, free tier) + Embeddings |
| Vector | pgvector (extension đã sẵn trong image Supabase) |
| ORM | EF Core 8 + Npgsql (chỉ quản lý `public.*`, không đụng `auth.*` / `storage.*`) |

**Đã bỏ (Phase 1 cũ → migrated):** Custom JWT service, BCrypt password hash, bảng `refresh_tokens`. Toàn bộ identity giờ do GoTrue quản lý trong schema `auth.*`. App chỉ giữ profile (`username`, `full_name`, `role_id`) trong `public.users` với FK `supabase_user_id → auth.users(id)`.

**Stack Supabase Local Phase 1 (7 services đang chạy):** `db`, `kong` (API gateway @ 8000), `auth` (GoTrue), `rest` (PostgREST), `meta`, `studio`, `analytics`. Phase 2 mở thêm `storage`, `realtime`, `functions`, `imgproxy`, `vector`, `supavisor` qua `--profile phase2`. Chi tiết → `06_Session_2026-05-24_Build_Handoff.md` Section 2.

Phase 1 trạng thái: **DONE** (auth migration + smoke test 5 endpoints + 3 edge case pass). Chi tiết state hiện tại → `02_Resume_Pack.md`.

---

## 3. Target Schema (Canonical)

Phase 1 đã apply: `public.roles`, `public.users` (mirror profile, FK `supabase_user_id`). Identity sống trong `auth.*` (GoTrue managed). Còn 11 bảng cần thêm dần qua Phase 2-4.

| # | Bảng | Schema | Phase add | Vai trò |
|---|---|---|---|---|
| — | `auth.users` + `auth.identities` + `auth.refresh_tokens` + `auth.sessions` | `auth` | **GoTrue native** | Identity, password hash, refresh rotation, session — **app KHÔNG đụng trực tiếp** |
| 1 | `roles` | public | 1 ✅ | Admin / Student |
| 2 | `users` | public | 1 ✅ | Profile mirror (`username`, `full_name`, `role_id`, `supabase_user_id` FK) — **không có `email`/`password_hash`** |
| 3 | `folders` | public | 2 | Folder cá nhân của user để gom documents |
| 4 | `documents` | public | 2 | Metadata file upload (Supabase Storage URL, owner, folder, status) |
| 5 | `document_chunks` | public | 2 | Chunked text + `embedding vector(N)` cho retrieval |
| 6 | `chat_sessions` | public | 3 | Phiên hội thoại của user (multi-doc context) |
| 7 | `chat_messages` | public | 3 | Messages trong session, role=user/assistant |
| 8 | `message_citations` | public | 3 | Citation từ assistant message → chunk + page number (Sub-RQ 3 nghiên cứu) |
| 9 | `rag_experiments` | public | 3 | A/B test config + metric cho research |
| 10 | `quizzes` | public | 4 | Quiz auto-generate từ documents |
| 11 | `quiz_questions` | public | 4 | Questions trong quiz, có loại (MCQ/short) |
| 12 | `user_answers` | public | 4 | Câu trả lời của user + scoring |
| 13 | `audit_logs` | public | 4 | Log admin action + user activity nhạy cảm |
| 14 | `system_settings` | public | 4 | Key-value config runtime (rate limit, AI model name, ...) |

> Note: Bảng `refresh_tokens` cũ trong `public.*` đã **bị drop** sau migration. GoTrue quản lý refresh token trong `auth.refresh_tokens` với rotation + reuse-detection sẵn (config qua `GOTRUE_REFRESH_TOKEN_REUSE_INTERVAL`).

### Naming convention chốt
- Postgres: snake_case lowercase (EF Core fold qua `EFCorePostgres.SnakeCaseConvention` trong `AppDbContext`)
- C# entity: PascalCase
- Timestamps: `created_at`, `updated_at` kiểu `TIMESTAMPTZ`, default `CURRENT_TIMESTAMP`
- Primary key: `id UUID PRIMARY KEY DEFAULT gen_random_uuid()` (dùng `gen_random_uuid()` từ extension `pgcrypto` đã sẵn)
- Foreign key: `<table>_id`, ON DELETE CASCADE cho ownership relations, ON DELETE SET NULL cho weak refs
- Vector: cột `embedding vector(N)` với N tùy embedding model (xem Section 5)
- **RLS:** `public.users` và `public.roles` đã ON RLS từ Phase 1. Phase 2+ mọi bảng public đều phải có policy hoặc bypass qua service-role key

---

## 4. Endpoint Contract (Phase 1 — đã ship)

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/auth/register` | Anonymous | GoTrue signup (autoconfirm on) + insert profile vào `public.users` |
| POST | `/api/auth/login` | Anonymous | GoTrue password grant → return access + refresh token |
| POST | `/api/auth/refresh` | Anonymous | GoTrue refresh grant (rotation + reuse detection ngoài 10s grace window) |
| POST | `/api/auth/logout` | Bearer | GoTrue signout `?scope=global` (revoke ALL refresh tokens của user) |
| GET | `/api/auth/me` | Bearer | User profile từ `public.users` + email từ JWT claim |

### JWT Claims (HS256, do GoTrue phát)
```
sub          = auth.users.id (UUID string) — Supabase user id
email        = auth.users.email
role         = "authenticated" (GoTrue native, không phải domain role)
app_metadata = { "role": "Admin" | "Student", "provider": "email", ... }
user_metadata = { "username": "...", "full_name": "..." }
aal          = "aal1"
session_id   = UUID
iss          = "<Supabase:JwtIssuer>"  (mặc định http://localhost:8000/auth/v1)
aud          = "authenticated"
iat / nbf / exp = unix seconds (access TTL ~1h GoTrue default)
```

> **Domain role mapping:** Trong `Program.cs` có `JwtBearerEvents.OnTokenValidated` parse `app_metadata.role` rồi promote thành `ClaimTypes.Role` để `[Authorize(Roles="Admin")]` hoạt động. Đừng tin claim top-level `role` vì GoTrue luôn set là `"authenticated"`.

Refresh token: opaque string do GoTrue cấp + lưu trong `auth.refresh_tokens`. Rotation tự động, reuse trong 10s đầu vẫn được chấp nhận (grace window cho retry), reuse sau 10s → 401 + chain-revoke.

---

## 5. Phase Roadmap

### Phase 1 — Auth Foundation **(DONE)**
Supabase Local Phase 1 stack (7 services), GoTrue identity, EF mirror profile, Blazor demo UI. Chi tiết state → `02_Resume_Pack.md`. Chi tiết migration session → `06_Session_2026-05-24_Build_Handoff.md`.

### Phase 2 — Document Management + RAG cơ bản (Sprint 2)
Cần Kiệt confirm trước khi bắt đầu:
- Supabase Storage bucket: tên, public/private, size limit. Bật qua `--profile phase2`
- Groq API key đã có chưa? Quota free tier?
- Embedding model: Groq alternative? Local sentence-transformers? **→ quyết định N của `vector(N)`**
- Chunking strategy: kích thước (token / char), overlap %, page-level metadata cho citation

Scope:
- Bật Phase 2 services: `docker compose --profile phase2 up -d` (storage, imgproxy, vector, ...)
- Add bảng `folders`, `documents`, `document_chunks` qua migration `AddDocumentSchema`
- `CREATE EXTENSION vector;` (đã có sẵn trong image Supabase, chỉ cần enable trong DB nếu chưa)
- Document upload UI (Blazor → backend → Supabase Storage REST API hoặc SDK)
- Background chunking + embedding job (HostedService hoặc on-demand)
- Vector search endpoint `POST /api/search` (cosine similarity, top-K)
- RAG endpoint cơ bản: `POST /api/chat/ask` → retrieve → prompt → Groq → response (chưa cần persist session)
- Tooling cân nhắc: Semantic Kernel hoặc gọi raw HTTP. **Recommend** raw HTTP cho Phase 2 để control chặt prompt + token budget.

### Phase 3 — Citation + Dashboard + Chat History (Sprint 3)
- Bảng `chat_sessions`, `chat_messages`, `message_citations`, `rag_experiments`
- Citation accuracy với page number metadata (Sub-RQ 3)
- Student dashboard (recent docs, recent chats, token usage)
- Chat history UI + multi-turn context

### Phase 4 — Admin Module + Quiz + Polish (Sprint 4)
- Bảng `quizzes`, `quiz_questions`, `user_answers`, `audit_logs`, `system_settings`
- Wire-up Admin UI (đã có sẵn trong `Components/Admin/` từ project cũ)
- Quiz auto-generate từ document content
- Audit logging cho admin actions
- Bug fix, demo prep, doc finalize

---

## 6. Dependencies Cần Thêm (Phase 2)

Dự kiến (sẽ confirm khi vào Phase 2):
- `UglyToad.PdfPig` hoặc `iText7` — PDF text extraction + page metadata
- HTTP client cho Groq (raw HTTPClient + typed client, không cần SDK riêng)
- HTTP client cho Supabase Storage REST API (hoặc `supabase-csharp` nếu muốn full SDK)
- Optional: `Microsoft.SemanticKernel` nếu chọn route SK

> Phase 1 đã add: `IGoTrueClient` (raw HTTP). Đã bỏ: `BCrypt.Net-Next`, custom `JwtTokenService`, `RefreshTokenService`, `PasswordHasher`.

---

## 7. Files Liên Kết

| File | Khi nào đọc |
|---|---|
| `02_Resume_Pack.md` | **Mỗi session mới** — state hiện tại + verification |
| `01_Architecture_Reference.md` (file này) | Khi planning Phase 2+ hoặc cần lookup schema |
| `05_Supabase_Local_Migration_Plan.md` | Plan migration v-final (source of truth quyết định L1-L13) |
| `06_Session_2026-05-24_Build_Handoff.md` | Build log session migration: deviations, smoke test, known issues |
| `archive/previous_session_raw_transcript.md` | Khi cần debug "session trước đã làm gì step-by-step" |
| `D:\FPT\summer2026\SWP391\AI_Study_Hub_Project_Overview.md` | Overview cũ của nhóm (cần update v2 sau Sprint 2) |
| `D:\FPT\summer2026\SWP391\SWP391_team_4.docx` | Sprint backlog + research proposal nhóm |

---

**End.**
