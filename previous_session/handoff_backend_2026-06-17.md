# Backend Handoff — 2026-06-17

## Architecture Overview

Blazor Server app (`.NET 8`) + PostgreSQL 15 (Docker Compose), EF Core 8 + Npgsql.

```
Browser ← SignalR → Blazor Server ← HTTP → Backend API (same process, [ApiController])
                          ↓
                     EF Core + Npgsql
                          ↓
                   PostgreSQL (Docker)
                          ↓
                   Supabase Storage (file blobs)
                          ↓
                   Groq Cloud (LLM: llama-3.1-8b-instant)
```

The app is a **single process**: the Blazor UI and the REST API run in the same ASP.NET Core host.  
API clients (`Services/*ApiClient.cs`) call the API via `HttpClient` pointing to the same host.

---

## Entity Model (PostgreSQL)

| Table | Columns | Notes |
|-------|---------|-------|
| `roles` | `id` (int PK), `role_name` (varchar unique), `description`, `created_at` | Seeded: Admin(1), Student(2) |
| `users` | `id` (guid PK), `role_id` (FK→roles), `supabase_user_id` (guid unique, FK→auth.users), `username` (varchar 15 unique), `full_name` (varchar 100), `total_tokens_used` (bigint), `is_active`, `created_at`, `updated_at` | Synced with Supabase Auth |
| `folders` | `id` (guid PK), `user_id` (FK→users), `name` (varchar 100), `description`, **`is_favorite`** (bool, default false), **`icon`** (varchar 50, nullable), **`is_shared`** (bool, default false), **`shared_at`** (timestamptz, nullable), `created_at`, `updated_at` | Unique per (user_id, name); cascade delete with user |
| `documents` | `id` (guid PK), `user_id` (FK→users), `folder_id` (FK→folders, SET NULL on delete), `file_name`, `storage_path` (unique), `file_size_bytes`, `mime_type`, `subject_code`, `semester`, `page_count`, `status` (enum: Uploading/Ready/Processing/Failed), `error_message`, `created_at`, `updated_at` | Indexed on (subject_code, semester) |
| `document_chunks` | `id` (guid PK), `document_id` (FK→documents, cascade), `chunk_index`, `page_number`, `content`, `token_count`, `embedding` (vector(384)), `created_at` | Unique per (document_id, chunk_index); IVFFlat cosine index on embedding |

**Bold** = columns added in the latest migrations (2026-06-17).

### Folder entity — UI contract
```csharp
// FolderDto returned to frontend:
Guid Id;
string Name;
string? Description;
int DocumentCount;          // computed: Documents.Count
bool IsFavorite;            // default false
bool IsShared;              // default false
DateTimeOffset? SharedAt;
string? Icon;               // material icon key, e.g. "folder", "school"
DateTimeOffset CreatedAt;
DateTimeOffset UpdatedAt;
string? OwnerName;          // only populated in shared folders (from User.FullName)
// Legacy fields that should still exist in DTO but unused by current UI:
string? Subject;
string? Semester;
string? Color;
string? BorderColor;
string? LastAccessText;
```

### Document entity — UI contract
```csharp
// DocumentDto returned to frontend:
Guid Id;
Guid? FolderId;
string FileName;
long FileSizeBytes;
string MimeType;
string SubjectCode;     // e.g. "SWP391"
string Semester;        // e.g. "SU26"
int? PageCount;
DocumentStatus Status;  // 0=Uploading, 1=Ready, 2=Processing, 3=Failed
string? ErrorMessage;
DateTimeOffset CreatedAt;
DateTimeOffset UpdatedAt;
string? DownloadUrl;    // Supabase signed URL
```

---

## API Controllers — Complete Contract

### AuthController (`/api/auth`)
| Method | Route | Auth | Request | Response |
|--------|-------|------|---------|----------|
| POST | `/api/auth/register` | AllowAnonymous | `{ email, username, fullName, password, recaptchaToken }` | `AuthResponse` (token pair + user) |
| POST | `/api/auth/login` | AllowAnonymous | `{ email, password, recaptchaToken }` | `AuthResponse` |
| POST | `/api/auth/refresh` | AllowAnonymous | `{ refreshToken }` | `AuthResponse` |
| POST | `/api/auth/logout` | Authorize | — | 200 OK |
| GET | `/api/auth/me` | Authorize | — | `UserDto` |

### FoldersController (`/api/folders`)
| Method | Route | Auth | Request | Response |
|--------|-------|------|---------|----------|
| GET | `/api/folders` | Authorize | — | `FolderDto[]` (user's folders, ordered by name) |
| GET | `/api/folders/shared` | AllowAnonymous | — | `FolderDto[]` (where `IsShared=true`, with `OwnerName`) |
| POST | `/api/folders` | Authorize | `{ name, description? }` | `FolderDto` |
| PUT | `/api/folders/{id}` | Authorize | `{ name?, description?, icon?, isFavorite?, isShared? }` | `FolderDto` |
| PATCH | `/api/folders/{id}/favorite` | Authorize | — | `FolderDto` (toggles `IsFavorite`) |
| PATCH | `/api/folders/{id}/share` | Authorize | — | `FolderDto` (toggles `IsShared` + sets `SharedAt`) |
| DELETE | `/api/folders/{id}` | Authorize | — | 204 (cascade deletes documents → chunks) |

**Business rules:**
- Folder names must be unique per user (case-sensitive in code, DB has unique index on `(user_id, name)`).
- Deleting a folder sets `folder_id = null` on its documents (SET NULL), then deletes the folder.
- The `/shared` endpoint returns ALL shared folders from ALL users — used by the Community page.

### DocumentsController (`/api/documents`)
| Method | Route | Auth | Request | Response |
|--------|-------|------|---------|----------|
| POST | `/api/documents/upload` | Authorize | `multipart/form-data`: file + `subjectCode`, `semester`, `folderId?` | `DocumentDto` |
| POST | `/api/documents/{id}/ingest` | Authorize | — | `DocumentDto` (status→Processing → Ready/Failed) |
| GET | `/api/documents` | Authorize | Query: `subjectCode`, `semester`, `folderId`, `q` | `DocumentDto[]` |
| GET | `/api/documents/{id}` | Authorize | — | `DocumentDto` |
| PUT | `/api/documents/{id}/folder` | Authorize | `{ folderId }` (nullable) | `DocumentDto` |
| DELETE | `/api/documents/{id}` | Authorize | — | 204 |

**Business rules:**
- File size limit: **50 MB** (defined in `DocumentApiClient.MaxFileSizeBytes`).
- Allowed MIME types: `application/pdf`, `application/msword`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.ms-powerpoint`, `application/vnd.openxmlformats-officedocument.presentationml.presentation`.
- Upload stores bytes in Supabase Storage, creates `Document` row (status=Uploading), triggers background ingestion: extract text → chunk → embed → persist chunks → status=Ready.
- Ingestion is synchronous in the current implementation (runs on the request thread).

### AiChatController (`/api/ai/chat`)
| Method | Route | Auth | Request | Response |
|--------|-------|------|---------|----------|
| POST | `/api/ai/chat/ask` | Authorize | `{ question, folderId?, documentIds?, subjectCode?, semester?, topK? }` | `{ answer, sources[], refusalReason?, durationMs }` |

**Business rules:**
- RAG pipeline: generate query embedding → pgvector cosine search → build prompt → call Groq → return answer + source excerpts.
- Uses `FakeEmbeddingService` for demos (deterministic 384-dim feature-hash, NOT real embeddings).
- `GroqChatCompletionClient` calls Groq Cloud's llama-3.1-8b-instant model.

### RagController (`/api/rag`)
| Method | Route | Auth | Request | Response |
|--------|-------|------|---------|----------|
| POST | `/api/rag/search` | Authorize | `{ query, documentId?, folderId?, subjectCode?, semester?, topK?, documentIds? }` | `RagSearchResultDto[]` |

### RolesController (`/api/roles`)
| Method | Route | Auth | Request | Response |
|--------|-------|------|---------|----------|
| GET | `/api/roles` | Authorize | — | `RoleDto[]` |

---

## UI → Backend Contracts (Critical for Frontend Compatibility)

### Document Upload Page (`/documents/upload`)
- **Sends**: `POST /api/documents/upload` with `multipart/form-data`: file + `subjectCode`, `semester`, `folderId`.
- **Subject code format** (validated client & server): must match `/^[A-Z]{2,4}\d{3,4}$/` (2-4 uppercase letters + 3-4 digits).
- **Semester format**: must match `/^(SPR|SU|FA|M1)\d{2}$/` (season code + 2-digit year).
- **Folder**: `folderId` is Guid, nullable (= loose document).
- **Accept query params**: `returnUrl` (for back navigation) and `folderId` (to pre-select folder).

### Workspace / AI Chat (`/ai/chat`)
- **Accepts query params**: `folderId` (guid) and `documentId` (guid) — from Library double-click or Document detail.
- **Sends**: `POST /api/ai/chat/ask` with `{ question, folderId?, documentIds?, topK? }`.
- When `folderId` is present in query: folder is **locked** (user cannot change folder in UI).
- When no `folderId`: user can freely switch between folders via dropdown.

### Folder Card (DocumentLibrary)
- **Double-click**: navigates to `/ai/chat?folderId={id}`.
- **Heart icon**: calls `PATCH /api/folders/{id}/favorite` (toggles).
- **Share action** (3-dot menu): calls `PATCH /api/folders/{id}/share` (toggles).
- **Rename**: calls `PUT /api/folders/{id}` with `{ name: "new name" }`.
- **Change icon**: calls `PUT /api/folders/{id}` with `{ icon: "icon_key" }`.
- **Delete**: calls `DELETE /api/folders/{id}`.

### Community Page (`/community`)
- **Calls**: `GET /api/folders/shared` (AllowAnonymous).
- **Click on shared folder**: navigates to `/ai/chat?folderId={id}`.

---

## Service Architecture (Dependency Injection)

All registered in `Program.cs`:

```
IFolderService → FolderService
IDocumentService → DocumentService
IAiChatService → SemanticKernelRagChatService
IAiChatCompletionClient → GroqChatCompletionClient
IRagSearchService → RagSearchService
ITextExtractionService → PdfTextExtractionService
IChunkingService → ChunkingService
IEmbeddingService → FakeEmbeddingService
IDocumentIngestionService → DocumentIngestionService
IDocumentStorageReadService → DocumentStorageReadService
IAuthService → SupabaseAuthService
IRoleCatalogService → RoleCatalogService
IRecaptchaVerificationService → RecaptchaVerificationService
IGoTrueClient → GoTrueClient
ISupabaseStorageClient → SupabaseStorageClient
AuthSessionState (scoped)
AiChatSessionState (scoped)
```

**API Clients** (registered as typed HttpClients):
- `FolderApiClient`
- `DocumentApiClient`
- `AuthApiClient`
- `AiChatApiClient`

These call the same host's API controllers. They exist because the Blazor pages are in the same process as the API — they could be refactored to call services directly.

---

## Configuration & Options

| Options Class | Section in appsettings.json | Key Values |
|---------------|---------------------------|------------|
| `SupabaseOptions` | `Supabase` | `Url`, `AnonKey`, `ServiceRoleKey` |
| `GroqOptions` | `Groq` | `ApiKey`, `Endpoint` (default: groq), `Model` (llama-3.1-8b-instant), `Temperature`, `MaxTokens`, `UseLocalDemoFallback` |
| `RagOptions` | `Rag` | `DefaultTopK: 5`, `MaxTopK: 20`, `MaxContextChars: 8000`, `ChunkSizeChars: 1500`, `ChunkOverlapChars: 200`, `EmbeddingDimensions: 384` |
| `RecaptchaOptions` | `Recaptcha` | `Enabled`, `SiteKey`, `SecretKey`, `AllowDevelopmentFallback: true` |
| `SeedOptions` | `Seed` | `SeedEnabled`, `DemoAdminEmail`, `DemoAdminPassword` |

---

## Migration Status

All migrations applied. Latest migrations (2026-06-17):
- `20260617031311_AddFolderFavoriteIcon` — adds `is_favorite`, `icon` to `folders`
- `20260617033024_AddFolderSharing` — adds `is_shared`, `shared_at` to `folders`

Both auto-apply on startup via `app.UseMigrateAsync<AppDbContext>()` in `Program.cs`.

---

## Known Issues & Gotchas

1. **FakeEmbeddingService** — Uses deterministic feature-hash. Fine for demos, but real semantic search requires a real embedding model (e.g., OpenAI text-embedding-3-small).
2. **Synchronous ingestion** — `POST /documents/{id}/ingest` blocks the request thread until extraction → chunking → embedding → persistence completes. For large PDFs this can take 10+ seconds.
3. **No pagination on GET /documents** — Returns ALL documents for the user. Could be a problem with 1000+ documents.
4. **Supabase Storage dependency** — Files must be uploaded to Supabase Storage before the API can process them. The upload endpoint streams directly to Supabase.
5. **GoTrueClient.AdminCreateUserAsync** — Requires `SupabaseOptions.ServiceRoleKey`, which has admin privileges. Keep this secret.
6. **MudBlazor CSS isolation** — Scoped CSS selectors use `b-xxx` attribute. MudPaper's root element may not always receive the scope attribute, causing some scoped styles to not apply. Workaround: wrap content in a plain `<div>` with the desired class.
7. **reCAPTCHA v3 site key** — Hardcoded in `_Imports.razor` and `recaptcha.js`. The key is `6LcglxotAAAAAJMIi0jZaLDtbPWuk9HUDeVTwH2x`.
8. **Subject code validation** — Client uses `/^[A-Z]{2,4}\d{3,4}$/`, server uses `/^[A-Z]{3}[0-9]{3}$/`. These should be aligned (client accepts 2-4 letters + 3-4 digits, server accepts exactly 3 letters + 3 digits).
