# Skill: AI Study Hub SWP391 Project Guide

## 0. Mục tiêu

File này là hướng dẫn làm việc cho agent khi tiếp tục dự án **AI Study Hub v2** của nhóm SWP391. Ưu tiên chính:

1. Giữ đúng kiến trúc hiện tại của dự án, không biến dự án thành stack khác.
2. Tiết kiệm context/token bằng cách đọc đúng file cần thiết, không paste/chỉnh toàn bộ repo.
3. Bảo toàn dữ liệu, secret, API contract và uncommitted work từ các session trước.
4. Mỗi thay đổi phải có verify phù hợp và ghi live log theo `previous_session/rule.md`.
5. Nếu gần chạm context/token limit, tự dừng làm việc mới và bắt đầu context-limit handoff theo `rule.md` Section 5.1.

## 1. Thông tin dự án hiện tại

```text
Repo/worktree chính: D:\FPT\summer2026\SWP391_github_current
Branch chính:        sprint2/upload-improve-and-ai-chat
Legacy worktree:     D:\FPT\summer2026\SWP391_parallel\s2_integration (reference only)
Project:             AI_Study_Hub_v2\AI_Study_Hub_v2.sln
Session logs:        D:\FPT\summer2026\SWP391\previous_session\
Startup state:       D:\FPT\summer2026\SWP391\previous_session\STARTUP_STATE.md
Session handoff:     D:\FPT\summer2026\SWP391\previous_session\SESSION.md
Full history:        D:\FPT\summer2026\SWP391\previous_session\SESSION_ARCHIVE.md
```

Trước khi làm việc trong worktree, luôn verify:

```powershell
pwd
rtk git branch --show-current
rtk git status --short --branch --untracked-files=all
```

Nếu đang tiếp tục UI redesign hiện tại, dùng live log:

```text
D:\FPT\summer2026\SWP391\previous_session\_CURRENT_SESSION_UI_REDESIGN.md
```

Không tự ý sửa handoff cũ đã đóng. Nếu task khác đang sở hữu `_CURRENT_SESSION.md`, dùng live log chuyên biệt đã được ghi trong handoff.

### Từ khóa `cập nhật SWP`

Khi user nói `cập nhật SWP`, chỉ làm bước nắm trạng thái read-only cho AI Study Hub / SWP391.

Bare `cập nhật` là mơ hồ khi có nhiều project startup (`SWP`, `nasus`); hỏi lại user muốn `cập nhật SWP` hay `cập nhật nasus` trước khi làm.

1. Đọc `SESSION.md`, `rule.md`, `skill.md`, `STARTUP_COMMANDS_HANG_GUARD.md`.
2. Ưu tiên đọc `_CURRENT_SESSION.md` nếu tồn tại. Nếu không có `_CURRENT_SESSION.md`, tìm và đọc session/handoff Markdown đã kết thúc gần nhất trong `previous_session\` (ưu tiên `NN_Session_*.md` có số NN lớn nhất; nếu không có thì dùng Markdown mới nhất theo thời gian sửa đổi).
3. Verify worktree/branch/status bằng lệnh read-only.
4. Không sửa code, không cập nhật tiến độ/live log, không chạy build/test/docker/migration có side effect.
5. Báo lại state hiện tại và 2-4 hướng đề xuất để tiếp tục, kèm recommended nếu rõ.
6. Chờ user chọn hướng rồi mới bắt đầu thực thi hoặc ghi tiến độ.

## 2. Stack kỹ thuật phải bám sát

| Lớp | Công nghệ hiện tại |
|---|---|
| Web app | ASP.NET Core / .NET 8 |
| UI | Blazor Razor Components, Interactive Server, MudBlazor 9.4 |
| Styling | Scoped `.razor.css` + `wwwroot/app.css`, dark premium purple/cyan glass theme |
| Auth | Supabase GoTrue, JWT Bearer, `AuthSessionState`, `SupabaseAuthService` |
| Database | PostgreSQL/Supabase, EF Core 8, Npgsql, pgvector |
| Storage | Supabase Storage wrapper |
| RAG | PDF text extraction via PdfPig, chunking, document ingestion, pgvector search |
| AI chat | RAG chat service + Groq chat completion client; fake embedding currently registered |
| Tests | NUnit, FluentAssertions, Moq, EF Core InMemory, WebApplicationFactory |

Không đề xuất React/Next/Tailwind/Vue trừ khi user yêu cầu rõ. Đây là Blazor + MudBlazor project.

## 3. File map nhanh

```text
AI_Study_Hub_v2/
├─ Program.cs                         # DI, auth, EF, RAG, controllers, Razor components
├─ Components/
│  ├─ App.razor                       # root providers/render mode/fonts
│  ├─ Layout/                         # MainLayout, NavMenu
│  ├─ Pages/                          # Home, Login, Register, Profile, Documents, AiChat
│  └─ Admin/                          # admin dashboard, users, documents, subjects, settings
├─ Controllers/                       # Auth, Documents, Folders, Rag, AiChat APIs
├─ Services/                          # app services + API clients + Supabase + RAG
├─ Services/Rag/                      # ingestion/search/chunking/extraction contracts
├─ Data/                              # AppDbContext, entities, EF configurations
├─ Options/                           # Supabase/RAG/Groq/Seed options
├─ Migrations/                        # EF migrations
├─ wwwroot/app.css                    # global design tokens and shared CSS
└─ AI_Study_Hub_v2.Tests/             # NUnit tests
```

Khi không biết file nằm đâu: dùng Glob/Grep trước, đọc file sau. Không đọc hàng loạt file lớn nếu chỉ cần một symbol.

## 4. Quy tắc bắt buộc khi sửa code

### 4.1 Không phá uncommitted work

- Worktree đang có nhiều file modified/untracked từ nhiều session.
- Luôn inspect diff của file trước khi sửa.
- Không revert, format toàn repo, xoá file, hoặc overwrite file nếu chưa hiểu nguồn gốc thay đổi.
- Với file đã bị session khác chạm vào, chỉ chỉnh đúng vùng cần thiết.

### 4.2 Không lộ secret

- Không in ra chat/log các giá trị user-secrets, JWT secret, anon/service role key, database password, Groq key.
- Nếu cần verify config, chỉ ghi dạng `present/missing`, không ghi value.
- Không commit `.env`, secrets, local config chứa credential.

### 4.3 Giữ API/backend contract

Khi chỉnh UI, không đổi endpoint/DTO/service behavior nếu user không yêu cầu.

Các luồng quan trọng cần giữ:

- Login/register -> `AuthApiClient` / `AuthSessionState`
- Upload/list/detail documents -> `DocumentApiClient`, `IDocumentService`
- Folder operations -> `FolderApiClient`, `IFolderService`
- AI chat -> `AiChatApiClient`, `IAiChatService`
- RAG ingestion/search -> `DocumentIngestionService`, `RagSearchService`

### 4.4 MudBlazor / render mode

- Provider status hiện tại cần giữ: `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider` ở root interactive app.
- Tránh duplicate provider trong layout/page nếu không có lý do rõ.
- Với Blazor Interactive Server, cẩn thận event handler, state, form validation, `@rendermode`, `AuthorizeView`.

### 4.5 C# conventions

- Nullable enabled: xử lý null rõ ràng.
- Dùng DI/interface có sẵn thay vì new trực tiếp service lớn.
- Async service/controller methods phải propagate cancellation nếu pattern hiện có dùng.
- Không swallow exception âm thầm trong business logic; map lỗi thành response/user message phù hợp.
- Không thêm package nếu có thể dùng package hiện có.

## 5. UI/UX direction hiện tại

User đã chọn hướng:

- Dark premium purple/cyan tone.
- Glass cards, subtle glow/orb motif.
- Layout Home giống forum/community dashboard kiểu FuOverflow:
  - top horizontal nav
  - centered fixed-width content
  - announcement/banner card
  - toolbar/community header
  - main discussion/table-style content left
  - right sidebar cards for profile/status/online info
- Preserve AI Study Hub functionality and backend/API contracts.

Khi polish UI:

1. Ưu tiên chỉnh đúng page/component đang bị feedback.
2. Dùng scoped CSS (`Page.razor.css`) cho page-specific styling.
3. Dùng `wwwroot/app.css` cho token/global utility/shared MudBlazor overrides.
4. Test desktop + mobile nếu thay layout/nav/form.
5. Không đổi route/link hiện có nếu không cần.

## 6. Workflow theo loại task

### 6.1 Task UI Blazor/MudBlazor

1. Read file liên quan: `.razor` + `.razor.css` + shared CSS nếu cần.
2. Xác định route/API/service đang dùng.
3. Chỉnh nhỏ, giữ event handler/API call hiện có.
4. Verify:

```powershell
rtk dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo
```

5. Nếu thay form/nav/interactive UI, chạy app preview:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --no-launch-profile --urls "http://localhost:5240"
```

6. Smoke các route tối thiểu: `/`, `/login`, `/register`, `/ai/chat`, `/documents` nếu có auth/session phù hợp.

### 6.2 Task API/service/backend

1. Read controller + service interface + service implementation + tests liên quan.
2. Không đổi database schema khi chưa cần; nếu cần migration thì log rõ.
3. Update/add tests gần nhất với behavior.
4. Verify:

```powershell
rtk dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo
rtk dotnet test "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo --no-build
```

### 6.3 Task RAG/upload/chat

Luồng cần hiểu trước khi sửa:

```text
PDF upload -> Supabase Storage -> Document row/status -> PdfTextExtractionService
-> ChunkingService -> DocumentChunk rows/vector -> RagSearchService
-> SemanticKernelRagChatService -> GroqChatCompletionClient -> AiChatController/UI
```

Checklist:

- Status document phải rõ: pending/processing/completed/failed theo enum hiện có.
- Không làm mất khả năng retry/debug ingestion.
- Với vector search, kiểm tra dimension và fake embedding/Groq boundary.
- Với chat, giữ citation/source behavior nếu đang có.
- Runtime smoke upload -> ingestion -> AI chat là follow-up quan trọng sau UI ổn định.

#### Runtime smoke không được treo live app

Khi user cho phép chạy runtime smoke, luôn chạy theo kiểu **bounded**:

1. Không chạy `dotnet run` foreground vô thời hạn.
2. Start app bằng background process, lưu PID vào temp file, ví dụ `C:\Users\pc\AppData\Local\Temp\opencode\aistudy-live\app.pid`.
3. Chờ port `5240` listen với timeout rõ ràng, mặc định tối đa 60 giây.
4. Mỗi bước smoke phải có timeout; nếu app/API/DB hang thì dừng smoke, ghi blocker vào live log.
5. Sau khi smoke pass hoặc fail, luôn stop app PID và verify `5240` không còn listener.
6. Nếu session tự start Supabase/Docker compose cho smoke, stop compose sau smoke trừ khi user bảo giữ lại.
7. Không để live app/Supabase chạy nền sau khi trả lời user nếu không có chỉ thị rõ.

Cleanup tối thiểu sau runtime smoke:

```powershell
Stop-Process -Id <appPid> -Force -ErrorAction SilentlyContinue
Get-NetTCPConnection -LocalPort 5240 -ErrorAction SilentlyContinue
```

Nếu đã start Supabase local stack trong session:

```powershell
docker compose -f infra\supabase\docker-compose.yml --project-directory infra\supabase stop
Get-NetTCPConnection -LocalPort 8000 -ErrorAction SilentlyContinue
```

Khi nhận yêu cầu “chạy runtime smoke”, hiểu mặc định là:

```text
start live app -> chạy smoke ngắn -> capture evidence -> cleanup/stop live app -> verify port stopped
```

Khi nhận yêu cầu “khởi động server tôi xem trực tiếp”, hiểu khác runtime smoke: được phép để app/Supabase chạy cho user xem, nhưng vẫn phải tuân thủ `STARTUP_COMMANDS_HANG_GUARD.md`: chạy app background bằng PID file, redirect logs, mọi wait có timeout, verify `/login` trả HTTP 200 trước khi báo URL, và nếu user báo đơ/interrupted thì hành động đầu tiên của session sau là stop PID + kiểm tra port/log.

### 6.4 Task database/migration

- Không drop/reset DB nếu user chưa xác nhận.
- Trước migration: ghi live log pre-flight.
- Verify migration bằng build/test và kiểm tra trạng thái DB ở mức không lộ secret.
- Nếu lỗi schema mismatch, báo rõ symptom + migration cần chạy; không tự ý phá data.

### 6.5 Task docs/planning/session

- Chỉnh đúng file user yêu cầu.
- Nếu là DOCX/binary, verify zip/readback.
- Nếu là session file, tuân thủ `rule.md`: append-only cho live log, handoff cũ immutable.

## 7. Live log rule tóm tắt

Sau mỗi hành động đáng kể phải cập nhật live log:

- Đọc context/handoff ban đầu.
- Sửa/tạo/xoá file.
- Chạy build/test/migration/docker/dev server.
- Gặp bug/blocker.
- User đổi hướng hoặc lock decision.
- Hoàn thành task.

Mỗi entry cần có timestamp UTC ISO 8601, ví dụ:

```markdown
### 2026-05-29T13:10Z - UI preview state verified
- Command: `rtk git status --short --branch --untracked-files=all` in integration worktree.
- Result: branch `sprint2/integration`, 30 modified, 17 untracked.
- Next: inspect diff before editing.
```

Không ghi “done” nếu chưa có evidence.

### 7.1 Context limit guard

Khi session gần hết context/token:

1. Update todo list trước để phản ánh đúng trạng thái hiện tại.
2. Không bắt đầu việc mới hoặc command dài; chỉ làm các bước cần thiết để tạo điểm dừng an toàn.
3. Ghi live log với marker `CONTEXT LIMIT GUARD`, gồm completed/in-progress/pending, evidence đã có, file changed, validation đã chạy, và exact next step.
4. Update `SESSION.md` nếu state meaningful đã đổi.
5. Nếu đang dùng `_CURRENT_SESSION.md` canonical và an toàn, chuyển sang trạng thái `CLOSING`/`PAUSED` và đóng/rename theo `rule.md`; nếu dùng task-specific live log thì cập nhật log đó và để lại resume instruction rõ.
6. Trả lời user ngắn gọn với nơi đã ghi handoff và cách resume.

## 8. Command cheat sheet

Chạy từ `D:\FPT\summer2026\SWP391_parallel\s2_integration` trừ khi ghi khác.

```powershell
# State
pwd
rtk git branch --show-current
rtk git status --short --branch --untracked-files=all

# Build/test
rtk dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo
rtk dotnet test "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo --no-build

# Run app preview
cd AI_Study_Hub_v2
$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --no-launch-profile --urls "http://localhost:5240"

# Check preview port
Get-NetTCPConnection -LocalPort 5240 -ErrorAction SilentlyContinue
```

Ưu tiên prefix shell commands bằng `rtk` khi phù hợp để giảm token. Không dùng `rtk` cho meta-command của chính rtk hoặc khi command cần raw output đầy đủ; lúc đó dùng `rtk proxy <cmd>` nếu muốn tracking.

## 9. Prompt templates phù hợp dự án

### 9.1 UI page/component update

```text
Task: Update Blazor UI for [page/component].
Context files:
- Components/Pages/[Page].razor
- Components/Pages/[Page].razor.css
- wwwroot/app.css only if shared tokens/global Mud override needed

Requirements:
- Preserve existing route, injected services, event handlers, API calls.
- Keep dark premium purple/cyan forum/glass direction.
- Make minimal edits; do not rewrite unrelated sections.
- Verify with dotnet build; run visual/http smoke if interaction/layout changed.
```

### 9.2 Backend/API behavior update

```text
Task: Change [specific behavior] in [controller/service].
Context files:
- Controllers/[X]Controller.cs
- Services/[X]Service.cs and interface
- Related DTO/entity/test files

Requirements:
- Preserve existing public contract unless explicitly changing it.
- Add/update NUnit + FluentAssertions tests.
- Verify build + test.
- Do not print secrets or reset database.
```

### 9.3 RAG runtime smoke

```text
Task: Smoke upload -> ingestion -> AI chat.
Requirements:
- Use Development environment.
- Do not expose secrets in output.
- Evidence needed: upload success, document status/chunks, chat answer with source/citation behavior.
- If failure: log symptom, likely layer, next command.
```

## 10. Anti-patterns cần tránh

- Dán hoặc đọc toàn bộ repo khi chỉ cần 1 component/service.
- Rebuild toàn bộ UI từ đầu thay vì chỉnh incremental.
- Đổi endpoint/DTO vì tiện cho UI mà không update backend/tests.
- Thêm framework frontend khác.
- Duplicate MudBlazor providers ở nhiều layout.
- Tự ý reset database, drop migration, xoá storage bucket/file.
- In secret/config value ra chat/live log.
- Commit/push khi user chưa yêu cầu.
- Sửa handoff cũ đã đóng.
- Bỏ qua live log sau khi thay đổi file hoặc chạy command có side effect.

## 11. Definition of Done tối thiểu

Một task được xem là xong khi:

1. File cần sửa đã được chỉnh đúng scope.
2. Live log đã cập nhật với evidence.
3. Build pass nếu có code change.
4. Test pass nếu backend/service/RAG/test change.
5. UI smoke/preview pass nếu thay layout/form/navigation quan trọng.
6. Không lộ secret, không revert uncommitted work, không đổi contract ngoài yêu cầu.

---

**Quy tắc vàng:** Bám sát stack Blazor/.NET 8 hiện tại, sửa nhỏ có kiểm chứng, ghi live log ngay, và không làm mất context của session trước.
