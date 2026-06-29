# AI Study Hub — Tài liệu Onboarding cho Người Mới

> **Version:** 2026-06-26
> **Branch:** `sprint2/upload-improve-and-ai-chat`
> **Dành cho:** Developer mới tham gia dự án, muốn hiểu hệ thống và bắt đầu code trong ngày.

---

## Mục lục

1. [Tổng quan dự án](#1-tổng-quan-dự-án)
2. [Stack công nghệ](#2-stack-công-nghệ)
3. [Kiến trúc tổng thể](#3-kiến-trúc-tổng-thể)
4. [Cài đặt & chạy local](#4-cài-đặt--chạy-local)
5. [Cấu trúc thư mục & file quan trọng](#5-cấu-trúc-thư-mục--file-quan-trọng)
6. [Các luồng dữ liệu chính](#6-các-luồng-dữ-liệu-chính)
7. [API map](#7-api-map)
8. [Database schema](#8-database-schema)
9. [Cách đóng góp](#9-cách-đóng-góp)
10. [Lưu ý & gotcha](#10-lưu-ý--gotcha)

---

## 1. Tổng quan dự án

**AI Study Hub** là nền tảng học tập thông minh cho phép sinh viên:

- **Upload tài liệu** (PDF, DOCX, PPTX, TXT) vào thư viện cá nhân
- **Chat với AI** dựa trên nội dung tài liệu (RAG — Retrieval-Augmented Generation)
- **Tạo quiz tự động** từ tài liệu đã upload (có filter theo môn học, học kỳ, từ khóa)
- **Quản lý thư mục**, chia sẻ tài liệu cho cộng đồng
- **Báo cáo nội dung** không phù hợp (community moderation)

### Ngữ cảnh dự án

- Dự án thuộc nhóm SWP391, học kỳ Summer 2026, Đại học FPT
- Repository: `SU26SWP10-AI-Study-Hub-team-4` trên GitHub
- Worktree chính: `D:\FPT\summer2026\SWP391_github_current`
- Branch đang phát triển: `sprint2/upload-improve-and-ai-chat`

---

## 2. Stack công nghệ

| Lớp | Công nghệ | Ghi chú |
|-----|-----------|---------|
| **Backend** | ASP.NET Core 8, C# 12 | Web API + Blazor Server |
| **Frontend** | Blazor Interactive Server, MudBlazor 9.4 | Razor Components, không dùng React/Vue |
| **CSS** | Scoped `.razor.css` + `wwwroot/app.css` | Dark premium purple/cyan glass theme |
| **Database** | PostgreSQL (Supabase) | Self-hosted qua Docker |
| **Vector DB** | pgvector extension | Lưu và tìm kiếm embedding 384 chiều |
| **ORM** | EF Core 8 | Code-first, migrations |
| **Auth** | Supabase GoTrue | JWT Bearer token |
| **Storage** | Supabase Storage | File upload qua signed URL |
| **AI Chat** | Groq API (Llama 3.3 70B) + Gemini 2.5 Flash | Model routing qua factory |
| **Embedding** | FakeEmbeddingService (demo) → sẽ thay bằng Ollama | 384-dim vector |
| **PDF** | PdfPig | Text extraction từ PDF |
| **Office** | OpenXML | DOCX, PPTX extraction |
| **Testing** | NUnit, FluentAssertions, Moq | InMemory EF, WebApplicationFactory |

---

## 3. Kiến trúc tổng thể

```
┌──────────────────────────────────────────────────────────────┐
│                     TRÌNH DUYỆT                              │
│  Blazor Interactive Server (SignalR)                         │
│  MudBlazor UI Components                                     │
└────────────────────────┬─────────────────────────────────────┘
                         │ HTTP + WebSocket
┌────────────────────────▼─────────────────────────────────────┐
│                  ASP.NET CORE 8                              │
│                                                              │
│  ┌──────────────┐  ┌──────────────────────────────────────┐ │
│  │ Controllers  │  │ Services                             │ │
│  │ (REST API)   │  │  ├─ AuthService                      │ │
│  │              │  │  ├─ DocumentService                  │ │
│  │ /api/auth    │  │  ├─ FolderService                    │ │
│  │ /api/docs    │  │  ├─ CommunityService                 │ │
│  │ /api/folders │  │  ├─ AiChatService (RAG orchestrator) │ │
│  │ /api/ai/chat │  │  ├─ QuizService                      │ │
│  │ /api/rag     │  │  ├─ RagSearchService                 │ │
│  │ /api/quiz    │  │  ├─ DocumentIngestionService         │ │
│  │ /api/comm..  │  │  ├─ EmbeddingService                 │ │
│  │ /api/roles   │  │  └─ ChatPersistenceService           │ │
│  └──────────────┘  └──────────────────────────────────────┘ │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ Data Layer (EF Core 8)                               │   │
│  │  AppDbContext → PostgreSQL + pgvector                │   │
│  └──────────────────────────────────────────────────────┘   │
└────────────────────────┬─────────────────────────────────────┘
                         │
┌────────────────────────▼─────────────────────────────────────┐
│               HẠ TẦNG BÊN NGOÀI                              │
│                                                              │
│  ┌────────────┐  ┌──────────────┐  ┌───────────────────┐   │
│  │ Supabase   │  │ Groq API     │  │ Supabase Storage  │   │
│  │ GoTrue     │  │ Llama 3.3    │  │ (file upload)     │   │
│  │ (Auth)     │  │ 70B Versatile│  │                   │   │
│  └────────────┘  └──────────────┘  └───────────────────┘   │
│                                                              │
│  ┌────────────┐  ┌──────────────┐                           │
│  │ Gemini API │  │ Ollama       │  ← SẼ THÊM (RBL Phase 1) │
│  │ 2.5 Flash  │  │ all-minilm   │                           │
│  └────────────┘  └──────────────┘                           │
└─────────────────────────────────────────────────────────────┘
```

### Pattern kiến trúc chính

Dự án dùng **Layered Architecture** với Dependency Injection:

```
Controller → Service Interface → Service Implementation → DbContext / External API
     ↑                              ↑
     │                              │
  DTOs (vào/ra)              Entities (DB)
```

- **Controllers**: nhận HTTP request, gọi service, trả DTOs
- **Services**: business logic, orchestration
- **Data/Entities**: EF entities map vào DB
- **Data/Configurations**: Fluent API config cho EF
- **Dtos**: request/response contracts
- **Services/*ApiClient.cs**: typed HttpClient cho Blazor gọi API

---

## 4. Cài đặt & chạy local

### Yêu cầu

- .NET 8 SDK
- Docker Desktop (cho PostgreSQL + Supabase services)
- Git

### Bước 1: Clone & chuyển branch

```powershell
git clone <repo-url>
git checkout sprint2/upload-improve-and-ai-chat
```

### Bước 2: Khởi động Supabase local (Docker)

```powershell
cd infra/supabase
docker compose up -d
# Chờ PostgreSQL + GoTrue + Storage sẵn sàng (~60s)
```

### Bước 3: Cấu hình secrets

Tạo User Secrets trong thư mục `AI_Study_Hub_v2/`:

```powershell
cd AI_Study_Hub_v2
dotnet user-secrets set "Supabase:AnonKey" "<key>"
dotnet user-secrets set "Supabase:ServiceRoleKey" "<key>"
dotnet user-secrets set "Supabase:JwtSecret" "<secret>"
dotnet user-secrets set "Groq:ApiKey" "<groq-api-key>"
dotnet user-secrets set "Gemini:ApiKey" "<gemini-api-key>"
dotnet user-secrets set "Recaptcha:SecretKey" "<key>"
dotnet user-secrets set "Seed:DefaultAdmin:Password" "<password>"
```

### Bước 4: Chạy migration

```powershell
# Migration tự động chạy khi app start (Program.cs)
# Hoặc chạy thủ công:
dotnet ef database update
```

### Bước 5: Chạy app

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls "http://localhost:5240"
```

Mở trình duyệt: `http://localhost:5240`

### Chạy test

```powershell
dotnet test AI_Study_Hub_v2.sln
```

---

## 5. Cấu trúc thư mục & file quan trọng

```
AI_Study_Hub_v2/
│
├── Program.cs                          # ⭐ DI, auth, middleware, migration, seed
├── appsettings.json                    # Cấu hình chung (không có secret)
│
├── Controllers/                        # ⭐ REST API endpoints
│   ├── AuthController.cs               #   POST register/login/refresh/logout, GET me
│   ├── DocumentsController.cs          #   CRUD documents + upload + ingest
│   ├── FoldersController.cs            #   CRUD folders + share/favorite/vote
│   ├── AiChatController.cs             #   POST ask, CRUD sessions
│   ├── QuizController.cs               #   POST generate, GET resume, PATCH save
│   ├── RagController.cs                #   POST search
│   ├── CommunityController.cs          #   POST report, GET pending, PATCH resolve
│   ├── RolesController.cs              #   GET list roles
│   └── BenchmarkController.cs          #   POST run benchmark
│
├── Services/                           # ⭐ Business logic
│   ├── IAiChatService.cs               #   Interface: AskAsync()
│   ├── SemanticKernelRagChatService.cs #   ⚠️ Tên misleading — KHÔNG dùng Semantic Kernel
│   ├── IAiChatCompletionClient.cs      #   Interface LLM client
│   ├── GroqChatCompletionClient.cs     #   Groq API client (Llama 3.3 70B)
│   ├── GeminiChatCompletionClient.cs   #   Gemini API client (2.5 Flash)
│   ├── AiChatCompletionClientFactory.cs#   Router model → provider
│   ├── GroqVisionDescriptionService.cs #   Mô tả ảnh trong PDF
│   ├── ChatPersistenceService.cs       #   Lưu session + message + quiz metadata
│   ├── AiChatSessionState.cs           #   Trạng thái chat trong Blazor circuit
│   ├── QuizService.cs                  #   Quiz generation + save + resume
│   │
│   ├── IDocumentService.cs             #   Interface quản lý tài liệu
│   ├── DocumentService.cs              #   Upload, list, move, delete, rename
│   ├── IFolderService.cs               #   Interface quản lý thư mục
│   ├── FolderService.cs                #   CRUD + share + favorite + vote + copy
│   ├── ICommunityService.cs            #   Interface báo cáo cộng đồng
│   ├── CommunityService.cs             #   Report, list pending, resolve
│   │
│   ├── SupabaseAuthService.cs          #   Auth với Supabase GoTrue
│   ├── RecaptchaVerificationService.cs #   Google reCAPTCHA
│   ├── RoleCatalogService.cs           #   Đọc danh sách roles từ DB
│   │
│   ├── *ApiClient.cs                   #   Typed HttpClient cho Blazor UI
│   │   AuthApiClient, DocumentApiClient, FolderApiClient,
│   │   AiChatApiClient, CommunityApiClient
│   │
│   ├── Rag/                            # ⭐ RAG Pipeline
│   │   ├── RagContracts.cs             #   TẤT CẢ interface: ITextExtraction, IChunking,
│   │   │                               #   IEmbedding, IRagSearch, IDocumentIngestion
│   │   ├── PdfTextExtractionService.cs #   Extract text từ PDF/DOCX/PPTX/TXT
│   │   ├── ChunkingService.cs          #   Cắt văn bản thành chunk chồng lấp
│   │   ├── FakeEmbeddingService.cs     #   Embedding giả (FNV-1a hash) — sẽ thay
│   │   ├── RagSearchService.cs         #   Tìm kiếm ngữ nghĩa qua pgvector
│   │   ├── DocumentIngestionService.cs #   ⭐ Orchestrator ingestion toàn bộ
│   │   ├── DocumentStorageReadService.cs#  Cầu nối Supabase Storage
│   │   └── Benchmarking/               #   Đánh giá chất lượng RAG
│   │
│   └── Supabase/                       # Supabase client tự viết
│       ├── GoTrueClient.cs             #   Auth API client
│       └── SupabaseStorageClient.cs    #   Storage API client
│
├── Data/                               # ⭐ Data layer
│   ├── AppDbContext.cs                 #   DbContext chính
│   ├── AppDbContextFactory.cs          #   Factory cho EF CLI
│   ├── Entities/                       #   11 entity classes
│   │   ├── User.cs, Role.cs
│   │   ├── Document.cs, DocumentChunk.cs, DocumentStatus.cs
│   │   ├── Folder.cs, FolderReaction.cs
│   │   ├── ChatSession.cs, ChatMessage.cs
│   │   ├── Quiz.cs, CommunityReport.cs
│   └── Configurations/                 #   Fluent API config cho từng entity
│
├── Dtos/                               # Request/response DTOs
│   ├── AuthDtos.cs, DocumentDtos.cs, FolderDtos.cs
│   ├── AiChatDtos.cs, ChatDtos.cs
│   ├── QuizDtos.cs, RagDtos.cs
│   ├── CommunityDtos.cs, RoleDtos.cs
│
├── Options/                            # IOptions config classes
│   ├── SupabaseOptions.cs, RagOptions.cs
│   ├── GroqOptions.cs, GeminiOptions.cs
│   ├── RecaptchaOptions.cs, SeedOptions.cs
│
├── Migrations/                         # 8 EF migrations (theo thời gian)
│
├── Components/                         # ⭐ Blazor UI
│   ├── App.razor                       #   Root component + MudBlazor providers
│   ├── Routes.razor                    #   Router config
│   ├── Layout/MainLayout.razor         #   Layout chính + NavMenu
│   ├── Pages/                          #   12 user-facing pages
│   │   ├── Home.razor                  #     /
│   │   ├── Login.razor                 #     /login
│   │   ├── Register.razor              #     /register
│   │   ├── AiChat.razor                #     /ai/chat — Chat AI + Quiz
│   │   ├── Community.razor             #     /community
│   │   ├── DocumentLibrary.razor       #     /documents
│   │   ├── DocumentUpload.razor        #     /documents/upload
│   │   ├── DocumentDetail.razor        #     /documents/{id}
│   │   └── Profile.razor               #     /profile
│   ├── Shared/                         #   Dialog components
│   │   └── Quiz/                       #   QuizDialog, QuizCard
│   └── Admin/                          #   9 admin pages
│       ├── Dashboard.razor             #     /admin
│       ├── Users/Users.razor           #     /admin/users
│       ├── Documents/                  #     /admin/documents/*
│       ├── Subjects/                   #     /admin/subjects
│       ├── Settings/                   #     /admin/settings
│       └── AuditLogs/                  #     /admin/audit-logs
│
├── wwwroot/
│   ├── app.css                         #   Global CSS + design tokens
│   ├── aichat.js                       #   JS interop cho chat
│   └── recaptcha.js                    #   reCAPTCHA callback
│
└── AI_Study_Hub_v2.Tests/              # Test project (NUnit)
    ├── Controllers/                    #   Controller tests
    ├── Services/                       #   Service tests
    └── Support/                        #   Test helpers
```

---

## 6. Các luồng dữ liệu chính

### 6.1 Upload & Ingest tài liệu

Đây là luồng **quan trọng nhất** cần hiểu.

```
1. User upload file
   POST /api/documents/upload (multipart form, 50MB max)
   → DocumentService.UploadAsync()
   → Upload lên Supabase Storage
   → Tạo Document row (Status = Ready)
   → Trả DocumentDto

2. Ingest (xử lý tài liệu) — tự động hoặc manual
   POST /api/documents/{id}/ingest
   → DocumentIngestionService.IngestAsync()
   │
   ├─ OpenReadAsync()           ← Lấy file từ Supabase Storage (signed URL → MemoryStream)
   ├─ ExtractPagesAsync()       ← PdfPig/OpenXML → text + images mỗi trang
   ├─ DescribeAsync()           ← Groq Vision → mô tả ảnh, gộp vào text trang
   ├─ Chunk()                   ← Cắt text thành chunk ~1000 ký tự, overlap 200
   │
   ├─ Với MỖI chunk:
   │  ├─ GenerateEmbeddingAsync() ← FakeEmbeddingService (FNV-1a hash → 384-dim)
   │  └─ INSERT DocumentChunk     ← Vector lưu vào pgvector
   │
   ├─ UPDATE Document.Status = Ready
   └─ Transaction commit
```

**File quan trọng:**
- `Services/Rag/DocumentIngestionService.cs:46-206` — orchestrator
- `Services/Rag/PdfTextExtractionService.cs` — extract text
- `Services/Rag/ChunkingService.cs` — chunking
- `Services/Rag/FakeEmbeddingService.cs` — embedding (sẽ thay bằng Ollama)

### 6.2 AI Chat với RAG

```
1. User gửi câu hỏi
   POST /api/ai/chat/ask
   {
     "question": "Giải thích khái niệm X",
     "documentIds": ["guid1", "guid2"],  // hoặc folderId
     "subjectCode": "SWP391",           // optional filter
     "semester": "SU2026",              // optional filter
     "topK": 5,
     "model": null                       // null = auto (Groq default)
   }

2. Controller (AiChatController)
   → Load ChatHistory từ DB (5 exchanges gần nhất)
   → Gán vào request.ChatHistory

3. Service (SemanticKernelRagChatService)
   │
   ├─ Step 1: Tìm kiếm ngữ cảnh
   │  → RagSearchService.SearchAsync()
   │  → Generate embedding cho câu hỏi (FakeEmbeddingService)
   │  → Query pgvector: CosineDistance → topK chunks
   │  → Filter theo userId, status=Ready, subjectCode, semester
   │
   ├─ Step 2: Xây dựng prompt
   │  → System prompt: "Bạn là trợ lý học tập..."
   │  → User prompt: câu hỏi + sources [S1][S2]... + chat history
   │  → Sources budget: MaxContextChars (6000 ký tự)
   │
   ├─ Step 3: Gọi LLM
   │  → AiChatCompletionClientFactory.GetClient(model)
   │  → GroqChatCompletionClient / GeminiChatCompletionClient
   │  → CompleteAsync()
   │
   └─ Step 4: Trả kết quả
      → AiChatAnswerResponse { answer, sources[], durationMs, sessionId }

4. Controller: lưu exchange vào DB
   → ChatPersistenceService.SaveExchangeAsync()
```

**File quan trọng:**
- `Controllers/AiChatController.cs:27-89` — endpoint
- `Services/SemanticKernelRagChatService.cs:55-146` — orchestrator
- `Services/Rag/RagSearchService.cs:30-69` — search
- `Services/GroqChatCompletionClient.cs` — Groq client
- `Services/GeminiChatCompletionClient.cs` — Gemini client

### 6.3 Quiz Generation

```
1. User bấm "Generate Quiz"
   → AiChat.razor gọi GenerateQuizAsync()
   → POST /api/quiz/generate

2. QuizService.GenerateAsync()
   │
   ├─ Step 1: Tìm ngữ cảnh
   │  → RagSearchService.SearchAsync() (với SubjectCode, Semester, TopicKeyword)
   │  → TopK = max(Count * 2, 5, 15)
   │
   ├─ Step 2: Tránh trùng câu hỏi cũ
   │  → Load TẤT CẢ quiz trước của user
   │  → Extract question texts → append "[EXCLUDED QUESTIONS]"
   │
   ├─ Step 3: Gọi LLM với prompt JSON schema
   │  → Yêu cầu LLM trả JSON:
   │    { title, questions: [{ text, options[A-D], correctOptionId, explanation }] }
   │
   ├─ Step 4: Parse + retry nếu JSON lỗi
   │  → Retry tối đa 2 lần, switch model nếu provider lỗi
   │
   └─ Step 5: Lưu kết quả
      → Quiz entity (questions_json, answers_json, submitted_json)
      → ChatPersistenceService.SaveQuizExchangeAsync()
```

**File quan trọng:**
- `Services/QuizService.cs:49-285` — toàn bộ logic quiz
- `Controllers/QuizController.cs` — endpoints
- `Dtos/QuizDtos.cs` — request/response

### 6.4 Community Report

```
1. User báo cáo thư mục
   POST /api/community/report { folderId, reason }

2. Admin xem danh sách pending
   GET /api/community/reports/pending  [Authorize(Roles = "Admin")]

3. Admin resolve
   PATCH /api/community/reports/{id}/resolve { status, resolution }
   Status: "Resolved" hoặc "Dismissed"
```

---

## 7. API Map

### Auth
| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| POST | `/api/auth/register` | Public | Đăng ký (có recaptcha) |
| POST | `/api/auth/login` | Public | Đăng nhập |
| POST | `/api/auth/refresh` | Public | Refresh token |
| POST | `/api/auth/logout` | JWT | Đăng xuất |
| GET | `/api/auth/me` | JWT | Profile người dùng hiện tại |

### Documents
| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| POST | `/api/documents/upload` | JWT | Upload file (multipart, 50MB) |
| GET | `/api/documents` | JWT | List tài liệu (có filter) |
| GET | `/api/documents/{id}` | JWT | Chi tiết tài liệu |
| PUT | `/api/documents/{id}/folder` | JWT | Di chuyển vào thư mục |
| PUT | `/api/documents/{id}/rename` | JWT | Đổi tên |
| GET | `/api/documents/{id}/file` | JWT | URL xem online (Office Viewer) |
| GET | `/api/documents/{id}/content` | JWT | Nội dung text đã extract |
| POST | `/api/documents/{id}/ingest` | JWT | Re-ingest tài liệu |
| DELETE | `/api/documents/{id}` | JWT | Xóa cứng |

### Folders
| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| GET | `/api/folders` | JWT | List thư mục của user |
| GET | `/api/folders/shared` | Public | List thư mục đã chia sẻ |
| GET | `/api/folders/personal-shared` | JWT | Thư mục mình đã share |
| POST | `/api/folders` | JWT | Tạo thư mục |
| PUT | `/api/folders/{id}` | JWT | Sửa thư mục |
| PATCH | `/api/folders/{id}/favorite` | JWT | Toggle yêu thích |
| PATCH | `/api/folders/{id}/share` | JWT | Toggle chia sẻ |
| POST | `/api/folders/{id}/vote` | JWT | Like/dislike |
| POST | `/api/folders/{id}/copy` | JWT | Copy thư mục đã share |
| DELETE | `/api/folders/{id}` | JWT | Xóa (cascade tài liệu) |

### AI Chat
| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| POST | `/api/ai/chat/ask` | JWT | Hỏi AI (có RAG) |
| GET | `/api/ai/chat/sessions` | JWT | List phiên chat |
| POST | `/api/ai/chat/sessions` | JWT | Tạo phiên mới |
| GET | `/api/ai/chat/sessions/{id}` | JWT | Xem messages |
| DELETE | `/api/ai/chat/sessions/{id}` | JWT | Xóa phiên |

### Quiz
| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| POST | `/api/quiz/generate` | JWT | Tạo quiz |
| GET | `/api/quiz/resume?sessionId=` | JWT | Resume quiz gần nhất |
| GET | `/api/quiz/{id}` | JWT | Xem quiz |
| PATCH | `/api/quiz/{id}/save` | JWT | Lưu tiến trình |

### RAG & Khác
| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| POST | `/api/rag/search` | JWT | Semantic search |
| POST | `/api/community/report` | JWT | Báo cáo thư mục |
| GET | `/api/community/reports/pending` | Admin | DS báo cáo chờ xử lý |
| PATCH | `/api/community/reports/{id}/resolve` | Admin | Xử lý báo cáo |
| GET | `/api/roles` | JWT | DS roles |
| POST | `/api/benchmark/run` | JWT | Chạy benchmark RAG |

---

## 8. Database Schema

### Bảng chính (11 bảng)

| Bảng | Mục đích | Cột quan trọng |
|------|----------|---------------|
| `roles` | Phân quyền | `id`, `role_name` (Admin/Student) |
| `users` | Người dùng | `id`, `supabase_user_id`, `role_id`, `is_active` |
| `folders` | Thư mục | `id`, `user_id`, `name`, `is_favorite`, `is_shared` |
| `documents` | Tài liệu | `id`, `user_id`, `folder_id`, `file_name`, `storage_path`, `subject_code`, `semester`, `status` |
| `document_chunks` | Chunk văn bản | `id`, `document_id`, `chunk_index`, `content`, `token_count`, `embedding` (**vector(384)**) |
| `chat_sessions` | Phiên chat | `id`, `user_id`, `folder_id`, `title`, `model`, `top_k` |
| `chat_messages` | Tin nhắn | `id`, `chat_session_id`, `role`, `content`, `metadata_json`, `sequence_number` |
| `quizzes` | Bài quiz | `id`, `session_id`, `user_id`, `title`, `status`, `questions_json`, `answers_json`, `submitted_json`, `score` |
| `folder_reactions` | Like/dislike | `folder_id` + `user_id` (composite PK), `is_like` |
| `community_reports` | Báo cáo | `id`, `folder_id`, `reported_by_user_id`, `reason`, `status`, `resolution` |

### Document Status Enum

```
Uploading(0) → Processing(2) → Ready(1)
                              → Failed(3)
```

### Quiz Status

```
InProgress → Completed
           → GeneratingFailed
```

### Index đặc biệt

```sql
-- Vector search index (IVFFlat)
CREATE INDEX ON document_chunks USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);
```

---

## 9. Cách đóng góp

### Quy trình làm việc

1. **Pull branch mới nhất:**
   ```powershell
   git checkout sprint2/upload-improve-and-ai-chat
   git pull origin sprint2/upload-improve-and-ai-chat
   ```

2. **Tạo branch riêng cho task:**
   ```powershell
   git checkout -b feat/ten-tinh-nang
   ```

3. **Code → Build → Test → Commit:**
   ```powershell
   dotnet build AI_Study_Hub_v2.sln
   dotnet test AI_Study_Hub_v2.sln
   git add <files>
   git commit -m "feat(scope): mô tả"
   ```

4. **Push & tạo PR:**
   ```powershell
   git push origin feat/ten-tinh-nang
   # Tạo PR trên GitHub vào sprint2/upload-improve-and-ai-chat
   ```

### Convention

| Việc | Convention |
|------|-----------|
| Commit message | `feat(scope):`, `fix(scope):`, `chore(scope):` |
| Branch name | `feat/`, `fix/`, `sprint2/` |
| C# | Nullable enabled, async/await, DI cho mọi service |
| CSS | Scoped `.razor.css` cho page-specific, `app.css` cho global |
| Test | NUnit + FluentAssertions, mock interface (không mock concrete) |

### File cần đọc khi thêm tính năng mới

| Muốn làm gì | Đọc file nào |
|-------------|-------------|
| Thêm API endpoint | Xem 1 controller hiện có → copy pattern |
| Thêm service | Xem interface + implementation hiện có |
| Thêm entity | Xem entity + configuration + migration hiện có |
| Sửa UI page | Đọc `.razor` + `.razor.css` + `*ApiClient.cs` |
| Sửa RAG/AI | Đọc `Services/Rag/RagContracts.cs` + `DocumentIngestionService.cs` |
| Thêm config | Xem `Options/` + `appsettings.json` + `Program.cs` |

---

## 10. Lưu ý & gotcha

### 10.1 Những cái tên misleading

| Tên | Thực tế |
|-----|---------|
| `SemanticKernelRagChatService` | **KHÔNG dùng Semantic Kernel** — tên cũ, chưa đổi. Là RAG orchestrator thuần |
| `FakeEmbeddingService` | Embedding giả dùng FNV-1a hash. Sẽ thay bằng Ollama `all-minilm:l6-v2` |
| `AuthSessionState` | Scoped service lưu JWT token trong Blazor circuit |

### 10.2 Những điểm dễ gây bug

1. **Transaction trong DocumentIngestionService**: nếu 1 chunk fail → toàn bộ tài liệu rollback. Không có retry per-chunk.

2. **ChunkSizeChars = 1000 quá lớn** cho embedding model 256-token limit. Sẽ giảm về 500 khi deploy Ollama.

3. **ChatHistory** được load ở Controller, truyền vào request DTO, rồi append vào user prompt. Không phải system context.

4. **Quiz khó khăn** (`"easy"/"medium"/"hard"`) chỉ ảnh hưởng text trong system prompt, không thay đổi logic.

5. **Community report status** là string (`"Pending"/"Resolved"/"Dismissed"`), không phải enum.

6. **AI model routing**: nếu model bắt đầu bằng `gemini` → Gemini API, còn lại → Groq API. Nếu Gemini key không có, auto fallback về Groq.

7. **Blazor Interactive Server**: state sống trong circuit (SignalR connection). Mất kết nối = mất state. Dùng `AiChatSessionState` cẩn thận.

### 10.3 Secret & config

- **KHÔNG commit** API keys, JWT secrets, passwords vào Git
- Dùng `dotnet user-secrets` hoặc environment variables
- File `appsettings.json` chỉ chứa config không nhạy cảm
- Secret đọc qua `IOptions<T>` pattern (xem `Options/` folder)

### 10.4 Migration

- Migration tự chạy khi app start (`db.Database.MigrateAsync()` trong Program.cs)
- KHÔNG tự ý xóa migration cũ
- Khi thêm entity mới: tạo entity → tạo Configuration → `dotnet ef migrations add <Tên>`
- DocumentChunk dùng `pgvector` extension — cần PostgreSQL có cài extension `vector`

---

## Phụ lục: Các task đang pending

| # | Task | Trạng thái |
|---|------|-----------|
| 1 | Push 5 commits lên origin | ✅ Đã push (2026-06-26) |
| 2 | Admin report review UI | API done, chưa có UI |
| 3 | RBL Phase 1 — Real Embedding (Ollama) | Đã review, chưa code |
| 4 | Bảo UI selective merge | Chưa làm |
| 5 | SCRUM-29/31/33 smoke test | Chưa làm |
| 6 | Quiz difficulty selector UI | Chưa làm |

---

> **Cần hỏi gì thêm?** Mở issue trên GitHub hoặc ping trong group.
