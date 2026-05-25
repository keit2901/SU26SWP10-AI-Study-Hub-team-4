# 07 — Phase 2 Document Management + RAG Plan (v-final)

**Status:** v-final LOCKED — Kiệt confirm Q1-Q10 = recommend toàn bộ vào 2026-05-25. GO BUILD.
**Author:** OpenCode (kr/claude-opus-4.7)
**Ngày soạn:** 2026-05-24 (v1 DRAFT) → 2026-05-25 (v-final lock)
**Pre-reqs:** Phase 1 Auth đã DONE (xem `02_Resume_Pack.md` Section 4 + `06_Session_2026-05-24_Build_Handoff.md`).
**Reference:** `01_Architecture_Reference.md` Section 3 (target schema) + Section 5 (roadmap).
**Sprint align:** Jira Sprint 1 (18 May - 1 Jun, 11 work items SCRUM-12..18, 25..28). 3/11 done (auth track). 8 items document track triển khai theo plan này.

---

## 0. Mục tiêu

Build Document Management + RAG cơ bản trên nền Supabase Local Phase 2 services. End-to-end:

1. User upload PDF qua Blazor → backend → **Supabase Storage** bucket
2. Background job: extract text (page-aware) → chunk → embed → lưu pgvector
3. User hỏi câu hỏi → backend retrieve top-K chunk → prompt Groq Llama 3.1 → response trả về
4. Bonus: Blazor UI overhaul fix "refresh page = logout" qua `ProtectedSessionStorage`

**Out of scope Phase 2** (đẩy Phase 3+ hoặc out hẳn):
- Chat session persistence + multi-turn (Phase 3)
- Citation accuracy + page metadata visualization (đã bỏ Sub-RQ 3 theo quyết định A3)
- Quiz auto-generation (Phase 4)
- Audit logging (Phase 4)
- Admin module wire-up (Phase 4)

---

## 1. Open Questions — CẦN KIỆT LOCK trước GO BUILD

| # | Câu hỏi | Recommend | Note |
|---|---|---|---|
| Q1 | **Groq API key đã có chưa?** Quota free tier? | Free tier 30 req/min, 14400 req/day cho Llama 3.1 8B. Đủ Phase 2 demo. | Cần Kiệt paste key vào `dotnet user-secrets` trước khi code. Lưu ở `Groq:ApiKey`. |
| Q2 | **Embedding model + dimension N của `vector(N)`?** | **Groq không có embeddings API.** Hai option: (a) `BAAI/bge-small-en-v1.5` (384 dim) chạy local qua `Microsoft.ML.OnnxRuntime` + ONNX model (~30MB) — offline, deterministic, free. (b) Gọi **OpenAI-compatible** endpoint khác (Ollama local nếu có, hoặc Cohere/Voyage trial). **Recommend (a)** cho Phase 2: zero key, zero quota, đủ chất lượng tiếng Anh + tiếng Việt FPT-friendly với multilingual variant. | Quyết định N này lock cột `embedding vector(N)` trong migration. Sai N = phải migration lại. |
| Q3 | **PDF text extraction library?** | **`UglyToad.PdfPig`** (MIT, lightweight ~2MB, page-aware text extract, FPT-friendly cho commercial sau này). Tránh iText7 vì AGPL — license phiền nếu sau này thương mại hoá. | Sách giáo khoa FPT thường text-based PDF, không cần OCR. Nếu gặp PDF scan → flag cho user, skip ingestion. |
| Q4 | **Chunking config?** | Size=1000 chars, overlap=200 chars (~20%), tách theo paragraph khi có thể, page metadata kèm theo từng chunk. | Default reasonable cho RAG paper studies. Có thể tune sau khi smoke test retrieval quality. |
| Q5 | **Storage backend?** | **Supabase Storage** (đã sẵn infra, bật `--profile phase2`). Bucket `documents`, **private** (RLS enforced), max file 50MB. | Alternative: filesystem local `infra/uploads/` — đơn giản hơn nhưng không demo được "cloud storage" cho hội đồng. |
| Q6 | **RAG model?** | **Groq Llama 3.1 8B Instant** (`llama-3.1-8b-instant`) cho Phase 2 demo. 70B nếu free quota cho phép. | Free tier ưu tiên 8B vì RPM cao hơn (30 vs 30, nhưng latency thấp hơn nhiều). |
| Q7 | **Vector index loại nào?** | `IVFFlat` với `lists=100` cho ~10K chunks (Phase 2 scale). HNSW chỉ khi >100K chunks. | pgvector default cosine distance, dùng `vector_cosine_ops`. |
| Q8 | **Supabase Storage bucket private hay public?** | **Private**. Backend ký URL signed (TTL 5 phút) khi user request download. Không expose public URL → tránh lộ tài liệu nhạy cảm. | RLS policy chỉ cho phép service-role key bypass. App backend dùng service-role để upload/download. |
| Q9 | **Phase 2 có làm Blazor UI overhaul không?** (fix "refresh = logout" bằng `ProtectedSessionStorage`) | **Có**, gộp vào step 11 plan này. Tốn ~2h, làm chung lúc rework UI cho document upload + chat panel. | Nếu skip → demo phải warn "không refresh tab". Hội đồng có thể trừ điểm UX. |
| Q10 | **Folder feature scope?** | Phase 2 chỉ CRUD folder (create, list, rename, delete CASCADE documents). Drag-drop document giữa folders đẩy sang Phase 3. | Đơn giản hoá Phase 2. Folder = bucket logic, 1 user N folders, mỗi document thuộc đúng 1 folder. |

> **Nguyên tắc:** mỗi câu Q1-Q10 trên đều có recommendation. Nếu Kiệt OK hết theo recommend → reply "GO với recommend" là đủ, không cần answer từng câu.

---

## 2. Tech Decisions — LOCKED 2026-05-25

> Kiệt confirm "GO với recommend toàn bộ" + "Sprint 1 giữ nguyên 11 items, cày 7 ngày". Q1-Q10 → L1-L10.

| # | Lock | Detail |
|---|---|---|
| L1 | **Groq free tier** | Key set xong trong `dotnet user-secrets` key `Groq:ApiKey` (2026-05-25). Quota free 30 req/min, 14400 req/day. |
| L2 | **Embedding `bge-small-en-v1.5` ONNX local, dim=384** | Chạy local qua `Microsoft.ML.OnnxRuntime`, model ~30MB, không tốn quota. Cột `document_chunks.embedding vector(384)`. |
| L3 | **`UglyToad.PdfPig` 0.1.10+** (MIT) | Page-aware extract. Skip OCR cho PDF scan, mark `status='failed'` + `error_message`. |
| L4 | **Chunk size=1000 chars, overlap=200 (~20%)**, ưu tiên break theo `\n\n` paragraph khi có | `token_count` estimate = `chars / 4` (rough). |
| L5 | **Supabase Storage** bucket `documents` private, max 50MB/file | Bật `--profile phase2`. App backend dùng service-role key upload/download. **Override Jira SCRUM-13** "Azure Blob" (đã edit Jira 2026-05-25). |
| L6 | **Groq `llama-3.1-8b-instant`** | Latency thấp + RPM cao. Fallback `llama-3.3-70b-versatile` nếu cần chất lượng cao hơn (slot phụ). |
| L7 | **`IVFFlat` index `lists=100`, `vector_cosine_ops`** | Đủ cho <10K chunks Phase 2. HNSW chỉ khi >100K. |
| L8 | **Bucket private + signed URL TTL 5 phút** | RLS policy chỉ cho service-role bypass. App ký URL khi user request download. |
| L9 | **UI overhaul gộp Phase 2** (step 11) | `ProtectedSessionStorage` thay `AuthSessionState` in-memory → fix "refresh = logout". |
| L10 | **Folder CRUD** (create/list/rename/delete CASCADE) | Drag-drop document giữa folders đẩy Phase 3. |

### 2.1 Schema additions cho Sprint 1 Jira (SCRUM-12, SCRUM-15)

Sprint 1 yêu cầu upload kèm **subject_code + semester** và filter search theo 2 trường này. Quyết định 2026-05-25 (Kiệt option A):

- Thêm 2 cột flat trực tiếp vào `public.documents`:
  - `subject_code TEXT NOT NULL` (vd: "SWP391", "PRN232")
  - `semester TEXT NOT NULL` (vd: "SU26", "FA25")
- Index riêng `ix_documents_subject_semester` để filter nhanh
- KHÔNG tạo bảng `subjects` riêng (Phase 3 normalize nếu cần admin manage subject list)
- Validation client + server: regex subject `^[A-Z]{3}[0-9]{3}$`, semester `^(SP|SU|FA|WI)[0-9]{2}$`

> Tránh phá schema Section 3.1: 2 cột này sẽ append vào `CREATE TABLE public.documents` migration.

---

## 3. Schema Diff

### 3.1 Migration mới: `AddDocumentSchema`

```sql
-- Bảng folders: cá nhân của user
CREATE TABLE public.folders (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    name        TEXT NOT NULL,
    description TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (user_id, name)
);
CREATE INDEX ix_folders_user_id ON public.folders(user_id);
ALTER TABLE public.folders ENABLE ROW LEVEL SECURITY;

-- Bảng documents: metadata file upload
CREATE TYPE public.document_status AS ENUM ('uploading','ready','processing','failed');

CREATE TABLE public.documents (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id          UUID NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    folder_id        UUID NULL REFERENCES public.folders(id) ON DELETE SET NULL,
    file_name        TEXT NOT NULL,           -- tên gốc user upload
    storage_path     TEXT NOT NULL UNIQUE,    -- path trong Supabase Storage bucket
    file_size_bytes  BIGINT NOT NULL,
    mime_type        TEXT NOT NULL,
    subject_code     TEXT NOT NULL,           -- L-add 2026-05-25: vd "SWP391" (Sprint 1 SCRUM-12/15)
    semester         TEXT NOT NULL,           -- L-add 2026-05-25: vd "SU26"
    page_count       INT NULL,                -- null khi chưa extract xong
    status           public.document_status NOT NULL DEFAULT 'uploading',
    error_message    TEXT NULL,               -- lý do nếu status='failed'
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_documents_user_id ON public.documents(user_id);
CREATE INDEX ix_documents_folder_id ON public.documents(folder_id);
CREATE INDEX ix_documents_status ON public.documents(status);
CREATE INDEX ix_documents_subject_semester ON public.documents(subject_code, semester);
ALTER TABLE public.documents ENABLE ROW LEVEL SECURITY;

-- Bảng document_chunks: text chunks + embeddings
CREATE EXTENSION IF NOT EXISTS vector;       -- idempotent

CREATE TABLE public.document_chunks (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id  UUID NOT NULL REFERENCES public.documents(id) ON DELETE CASCADE,
    chunk_index  INT NOT NULL,                -- 0-based, thứ tự trong document
    page_number  INT NULL,                    -- page chứa chunk (nếu detect được)
    content      TEXT NOT NULL,               -- raw text chunk
    token_count  INT NULL,                    -- estimate cho prompt budget
    embedding    vector(<N>) NOT NULL,        -- N chốt sau Q2
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (document_id, chunk_index)
);
CREATE INDEX ix_document_chunks_document_id ON public.document_chunks(document_id);
CREATE INDEX ix_document_chunks_embedding
    ON public.document_chunks
    USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);
ALTER TABLE public.document_chunks ENABLE ROW LEVEL SECURITY;
```

> **Lưu ý:** EF Core sẽ generate `Migrations/<timestamp>_AddDocumentSchema.cs`. ENUM type cần custom mapping trong `OnModelCreating` (Npgsql hỗ trợ via `HasPostgresEnum`).

### 3.2 Entities mới (C#)

```
Data/Entities/
├── Folder.cs              ← Id, UserId, Name, Description, CreatedAt, UpdatedAt
├── Document.cs            ← + FolderId?, FileName, StoragePath, FileSizeBytes, MimeType, PageCount?, Status, ErrorMessage?
├── DocumentChunk.cs       ← + DocumentId, ChunkIndex, PageNumber?, Content, TokenCount?, Embedding (Pgvector.Vector)
└── DocumentStatus.cs      ← enum: Uploading, Ready, Processing, Failed
```

### 3.3 RLS Policies (Phase 2 baseline)

Phase 1 đã `ENABLE` nhưng chưa có policy → chỉ service-role bypass được. Phase 2 add policy đơn giản:

```sql
-- folders: user chỉ thấy folder của mình (qua supabase_user_id mapping)
CREATE POLICY folders_owner_all ON public.folders
    FOR ALL TO authenticated
    USING (user_id IN (SELECT id FROM public.users WHERE supabase_user_id = auth.uid()))
    WITH CHECK (user_id IN (SELECT id FROM public.users WHERE supabase_user_id = auth.uid()));

-- documents + chunks: tương tự, qua document.user_id
-- (chi tiết policy sẽ viết khi execute)
```

> Phase 2 backend hiện vẫn chạy qua **service-role key** (bypass RLS), policy này là defense-in-depth + chuẩn bị cho Phase 4 wire-up Admin UI cần phân quyền chặt.

---

## 4. Supabase Stack Changes

### 4.1 Bật Phase 2 services

```powershell
docker compose -f D:\FPT\summer2026\SWP391\infra\supabase\docker-compose.yml --profile phase2 up -d
```

Services thêm: `storage`, `imgproxy`, `realtime`, `functions` (edge runtime), `vector` (log shipper), `supavisor` (pooler — vẫn skip cho EF, nhưng có sẵn nếu cần test pooled).

App **chỉ dùng** `storage` và Postgres. `realtime`/`functions` không cần Phase 2 nhưng bật cùng vì cùng profile.

### 4.2 Storage bucket setup

Bucket `documents`:
- Private (RLS enforced)
- File size limit 50MB (config qua Storage env: `STORAGE_FILE_SIZE_LIMIT`)
- MIME types allowed: `application/pdf` only (Phase 2). Phase 3+ thêm `.docx`, `.txt`.
- Init script: thêm vào `infra/supabase/volumes/db/init/02-buckets.sql`:
```sql
INSERT INTO storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
VALUES ('documents', 'documents', false, 52428800, ARRAY['application/pdf']::text[])
ON CONFLICT (id) DO NOTHING;
```

### 4.3 Resource budget

Profile Phase 2 thêm ~1.5GB RAM nữa (storage 200MB, realtime 300MB, functions 400MB, supavisor 200MB, imgproxy 100MB, vector 200MB). Tổng stack ~3GB. Máy Kiệt còn dư hay cần upgrade Docker memory limit?

---

## 5. .NET Package Additions

| Package | Version | Vai trò |
|---|---|---|
| `UglyToad.PdfPig` | 0.1.10 | PDF text extract page-aware |
| `Microsoft.ML.OnnxRuntime` | 1.20.x | Run ONNX embedding model local (nếu chọn Q2.recommend) |
| `Microsoft.ML.Tokenizers` | 1.0.x | Tokenize cho BGE model |
| `Pgvector.EntityFrameworkCore` | 0.2.0 | (đã có) — thêm `HasPostgresExtension("vector")` + `Vector` type mapping |

> **KHÔNG add** `supabase-csharp` SDK Phase 2. Storage SDK quá nhiều dependencies, raw `HttpClient` đơn giản hơn cho 4 endpoint cần dùng (upload, download signed URL, delete, list).

### 5.1 Embedding model file

Download `bge-small-en-v1.5` ONNX từ HuggingFace, lưu ở `AI_Study_Hub_v2/Models/bge-small-en-v1.5.onnx` (~130MB) + `tokenizer.json` (~700KB). Add vào `.gitignore` nếu >50MB (GitHub limit). Recommend: lưu ngoài repo ở `D:\FPT\summer2026\SWP391\models\` và config path qua appsettings.

---

## 6. Endpoint Contract (Phase 2 — sẽ ship)

| Method | Path | Auth | Body / Query | Response |
|---|---|---|---|---|
| POST | `/api/folders` | Bearer | `{ name, description? }` | `FolderDto` |
| GET | `/api/folders` | Bearer | — | `FolderDto[]` (của user hiện tại) |
| PUT | `/api/folders/{id}` | Bearer | `{ name, description? }` | `FolderDto` |
| DELETE | `/api/folders/{id}` | Bearer | — | 204 |
| POST | `/api/documents` | Bearer | multipart: `file`, `folderId?` | `DocumentDto` (status=uploading→ready async) |
| GET | `/api/documents` | Bearer | `?folderId=&status=` | `DocumentDto[]` |
| GET | `/api/documents/{id}` | Bearer | — | `DocumentDto` (full + chunks count) |
| GET | `/api/documents/{id}/download` | Bearer | — | 302 redirect tới Storage signed URL TTL 5 phút |
| DELETE | `/api/documents/{id}` | Bearer | — | 204 (cascade chunks + storage object) |
| POST | `/api/search` | Bearer | `{ query, folderId?, topK=5 }` | `SearchResultDto[]` (chunk + score + document meta) |
| POST | `/api/chat/ask` | Bearer | `{ question, folderId?, topK=5 }` | `{ answer, citedChunks[] }` (sync, không persist session Phase 2) |

---

## 7. Execution Steps

### 7.1 Stack + DB (step 1-3)
1. **Backup hiện tại:** `docker exec supabase-db pg_dump -U postgres postgres > backups/supabase_phase1_<date>.sql`
2. **Bật Phase 2 services:** `docker compose --profile phase2 up -d`. Verify: `docker compose ps` thấy `supabase-storage` healthy.
3. **Thêm bucket init script:** edit `infra/supabase/volumes/db/init/`, thêm `02-buckets.sql`. Restart `supabase-db` chỉ khi đầu tiên (db-init scripts chỉ chạy 1 lần lúc volume rỗng). **Cẩn thận:** hiện tại volume đã có data → phải INSERT thủ công vào storage.buckets, đừng restart wipe data.

### 7.2 .NET code (step 4-9)
4. Add packages (Section 5). Verify build clean.
5. Tạo entities + DbContext config + DTOs.
6. Tạo migration `AddDocumentSchema`. Verify SQL preview (`dotnet ef migrations script`) trước apply.
7. Apply: `dotnet ef database update`. Verify DB có 3 bảng + index ivfflat.
8. Implement services:
   - `IStorageClient` + `SupabaseStorageClient` (raw HTTP) — upload, signedUrl, delete
   - `IPdfTextExtractor` + `PdfPigExtractor` — extract per page
   - `IEmbeddingService` + `OnnxEmbeddingService` — load model, embed batch
   - `IChunkingService` + `RecursiveChunker` — 1000 chars, 200 overlap, paragraph-aware
   - `IDocumentIngestionService` — orchestrator: download → extract → chunk → embed → save
   - `ISearchService` — embed query → pgvector cosine top-K
   - `IGroqClient` + `GroqClient` — chat completion
   - `IRagService` — search + prompt template + Groq → response
9. Implement controllers (`FoldersController`, `DocumentsController`, `SearchController`, `ChatController`). Wire-up DI trong `Program.cs`.

### 7.3 Background processing (step 10)
10. **Document ingestion strategy:** đơn giản nhất Phase 2 = **synchronous** (upload → extract → chunk → embed inline trong request). PDF nhỏ <5MB chạy <10s OK. Khi >5MB hoặc nhiều trang → tách `IBackgroundIngestionQueue` (in-memory `Channel<Guid>` + `BackgroundService` worker).
    - **Recommend Phase 2:** sync với timeout 30s. Document lớn hơn → return 202 Accepted + status='processing', đẩy queue. Phase 3 nâng cấp lên Hangfire/Quartz.

### 7.4 Blazor UI overhaul (step 11)
11. **Fix "refresh = logout"** + thêm document UI:
    - Inject `ProtectedSessionStorage` (Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage). Lưu access + refresh token + user info.
    - `AuthSessionState` đổi từ in-memory → đọc/ghi `ProtectedSessionStorage` trên `OnInitializedAsync`.
    - Auto refresh token khi access token expired (background timer hoặc on-401 retry).
    - Thêm pages: `/documents`, `/documents/upload`, `/documents/{id}`, `/chat`.
    - Thêm components: `FolderTree`, `DocumentList`, `UploadForm`, `ChatPanel`, `CitationViewer` (basic: hiện chunk text + page).

### 7.5 Smoke test + docs (step 12-14)
12. Smoke test:
    - Upload PDF nhỏ (~1MB, 5 pages) → status ready trong <15s, có chunks trong DB
    - Search query → top-5 chunk relevant
    - Chat ask → Groq response coherent + reference đúng chunk
    - Folder CRUD
    - Document delete → cascade chunks + storage object xoá
13. Update `02_Resume_Pack.md` Section 4 (Phase 2 done), Section 9 (schema thêm 3 bảng).
14. Tạo file handoff `09_Session_<date>_Phase2_Build_Handoff.md` lưu deviations + smoke results + known issues (giống file 06).

---

## 8. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | ONNX model load chậm lần đầu (~3-5s) | Singleton service, load 1 lần lúc app start |
| R2 | Embedding inference CPU-only chậm với batch lớn | Batch size 32, truncate input 512 tokens, parallelism degree=Environment.ProcessorCount |
| R3 | Groq free tier rate limit (30 req/min) | App-side throttle qua `SemaphoreSlim`, return 429 với Retry-After |
| R4 | PDF scan/image-based → PdfPig extract empty | Detect empty text → mark status=failed + error_message. Phase 3 cân nhắc OCR (Tesseract). |
| R5 | Migration ENUM `document_status` revert phức tạp | Down migration drop bảng theo thứ tự ngược, drop type cuối |
| R6 | Storage bucket init script chạy lúc volume đã có data → no-op | Phải INSERT thủ công 1 lần, document trong README |
| R7 | RAM tổng stack ~3GB có thể vượt Docker default 2GB | Docker Desktop → Settings → Resources → Memory ≥6GB |
| R8 | Vector index `ivfflat lists=100` quá thấp/cao | Phase 2 demo data nhỏ <10K chunks, `lists=100` OK. Tune sau |
| R9 | Blazor UI overhaul vỡ login flow hiện tại | Branch riêng `feat/phase2-ui`, smoke test login/logout sau mỗi lần touch `AuthSessionState` |
| R10 | EF + Pgvector + ENUM combo có edge case khi migration | Verify SQL script bằng `dotnet ef migrations script` trước khi update DB |

---

## 9. Acceptance Criteria

Phase 2 coi như xong khi:
1. Stack `--profile phase2` up clean, storage bucket `documents` ready
2. Migration `AddDocumentSchema` apply OK, 3 bảng + ivfflat index có
3. `dotnet build` 0 warning 0 error, `dotnet test` 3/3 (unchanged)
4. Smoke test 11 endpoints (Section 6) — tất cả 200/204/302 đúng
5. Demo flow: register → upload PDF → search query → chat ask → answer kèm citation chunks. Working end-to-end.
6. Blazor refresh tab giữ session (không logout) sau khi fix `ProtectedSessionStorage`
7. Docs `02_Resume_Pack.md` + `01_Architecture_Reference.md` đã sync schema mới
8. File handoff `09_Session_<date>_Phase2_Build_Handoff.md` viết xong với deviations + smoke results

---

## 10. Estimate

| Phase | Effort |
|---|---|
| Stack + DB setup (step 1-3) | 30-45 phút |
| Entities + migration + DbContext (step 4-7) | 60-90 phút |
| Services (step 8) | 4-6 giờ (8 service mới, ONNX setup là phần khó nhất) |
| Controllers + DI (step 9) | 60 phút |
| Background ingestion (step 10) | 60 phút |
| Blazor UI overhaul + document/chat pages (step 11) | 4-5 giờ |
| Smoke test + docs (step 12-14) | 90 phút |
| **Total** | **~14-18 giờ** |

Buffer 30% cho lần đầu xài ONNX + Supabase Storage → kế hoạch **2.5-3 ngày làm việc** (8h/ngày). Nếu sprint deadline gấp → cắt step 11 (Blazor UI) thành Phase 2.5 riêng, demo backend qua Postman trước.

---

## 11. Dependencies cho Phase 3+ (planned, không thuộc Phase 2)

- `chat_sessions`, `chat_messages` bảng — Phase 3
- Multi-turn context truyền qua history → re-rank chunks
- Citation viewer hiển thị PDF page với highlight (cần PDF.js trong Blazor)
- Token usage tracking → update `users.total_tokens_used` mỗi lần chat
- Admin module wire-up + audit logs — Phase 4

---

## 12. v1 → v-final Changelog

**2026-05-25 (Session B):**
- Section 1 Q1-Q10 → Section 2 L1-L10 lock theo recommend toàn bộ (Kiệt confirm)
- Section 2.1 NEW: thêm 2 cột `subject_code` + `semester` vào `public.documents` để khớp Jira SCRUM-12/15 (option A flat, không tạo bảng `subjects` riêng)
- Section 3.1 schema `documents` table: append 2 cột mới + index `ix_documents_subject_semester`
- Header status: v1 DRAFT → v-final LOCKED. Ghi chú align Sprint 1 Jira (11 items, 3 done, 8 còn).
- Groq API key đã set vào `dotnet user-secrets` key `Groq:ApiKey` (2026-05-25 21:0x ICT)

---

**END v1.** Action ngay: Kiệt review Section 1, reply "GO với recommend" hoặc liệt kê thay đổi từng câu.
