# _CURRENT_SESSION - codebase_read

**Started:** 2026-06-29T06:58:29.2472032+07:00
**Agent:** Codex (GPT-5)
**Goal:** Read the active codebase end-to-end enough to build a reliable working map of the system for follow-up implementation work.
**Status:** IN_PROGRESS

---

## 0. Context loaded
- [x] `AGENTS.md` (read from repo root on 2026-06-29)
- [x] `previous_session/handoff_backend_2026-06-20.md` (read 2026-06-29)
- [x] `previous_session/rule.md` (read 2026-06-29)
- [ ] `previous_session/skill.md` (missing in this repo snapshot)
- [x] `previous_session/_CURRENT_SESSION.md` (read 2026-06-29 for latest in-flight context)

## 1. Verified state at start
- `git status --short --branch --untracked-files=all` -> `## Front_End...origin/Front_End`
- Active solution found: `AI_Study_Hub_v2/AI_Study_Hub_v2.sln`

## 2. Plan
1. Read bootstrapping and configuration files.
2. Read data layer, services, controllers, and UI components in the active solution.
3. Read test coverage to understand intended behavior and known seams.
4. Summarize architecture, main flows, and risks for the next task.

## 3. Progress log (append-only, newest last)

### 2026-06-29T06:58:29.2472032+07:00 - Session opened
- Loaded repo instructions and current handoff context.
- Confirmed `AI_Study_Hub_v2` is the active .NET 8 solution and `AI_Study_HUB` appears to be a separate scaffold/sample tree.
- Read `AI_Study_Hub_v2/Program.cs`, `AI_Study_Hub_v2/AI_Study_Hub_v2.csproj`, and `AI_Study_Hub_v2/appsettings.json` to map startup, DI, storage, auth, RAG, and UI wiring.

### 2026-06-29T07:06:33.4614325+07:00 - Architecture map completed
- Read active data layer: `AppDbContext`, entity/configuration classes, migrations-oriented enum/vector setup, and ownership model around `public.users`, `folders`, `documents`, `document_chunks`, `chat_sessions`, `chat_messages`, `quizzes`, `folder_reactions`, `community_reports`.
- Read service layer end-to-end: Supabase auth + GoTrue wrapper, storage-backed document CRUD, folder/community logic, chat persistence, quiz generation, RAG extraction/chunking/embedding/search/ingestion, provider routing, Groq/Gemini clients, and reCAPTCHA verification.
- Read API layer end-to-end: `AuthController`, `DocumentsController`, `FoldersController`, `AiChatController`, `QuizController`, `CommunityController`, `RagController`, `BenchmarkController`, `RolesController`.
- Read UI/runtime layer: routing/layout shell, auth pages, profile, home, document upload/library, community page, AI chat workspace, and frontend API/state wrappers used by Blazor pages.
- Read test surface: service/controller tests plus contract tests for auth, documents, folders, RAG, AI chat, chunking, extraction, ingestion, embeddings, and smoke coverage.
- Working conclusion: the repo contains one real integrated product in `AI_Study_Hub_v2`; other trees are supporting artifacts, experiments, or older scaffolds.

### 2026-06-29T07:17:34.1045279+07:00 - Frontend-only upload UX fix
- Edited `AI_Study_Hub_v2/Components/Pages/DocumentUpload.razor` only.
- Synced upload-page subject/semester validation to the actual API contract:
  - subject now requires exactly `AAA999` shape like `SWP391`
  - semester now requires `SP|SU|FA|WI` + 2 digits like `SU26`
- Tightened frontend gating:
  - upload button now depends on explicit subject/semester validity instead of relying on the mixed `MudForm` state alone
  - submit path now normalizes input to uppercase and blocks invalid metadata before calling the API
- Improved upload error messaging for server-side `500` responses so students do not assume the problem is caused by their role or filename.
- Verification attempt:
  - `dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo` -> failed at restore due blocked network/NuGet access (`NU1301` to `https://api.nuget.org/v3/index.json`)
  - `dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo --no-restore` -> same `NU1301` in this environment

## 4. Files changed this session
| Path | Change |
|---|---|
| `previous_session/_CURRENT_SESSION_CODEBASE_READ.md` | Created live session log for repo-wide code reading |

## 5. Commands run (only commands with side effects)
- None

## 6. Decisions locked
- Use `previous_session/_CURRENT_SESSION_CODEBASE_READ.md` instead of overwriting the existing `_CURRENT_SESSION.md`.

## 7. Open questions / risks
- `previous_session/skill.md` referenced by instructions is absent in this repo snapshot.
- The repo contains multiple app trees; follow-up changes should target `AI_Study_Hub_v2` unless evidence shows otherwise.

## 8. Next step (if pause/crash now)
Read the active solution vertically in this order: `Data` -> `Services` -> `Controllers` -> `Components` -> `AI_Study_Hub_v2.Tests`, then update this log with the resulting architecture map.

## 9. Quick Facts (snapshot)
- Git: `Front_End` tracking `origin/Front_End`
- Active solution: `AI_Study_Hub_v2/AI_Study_Hub_v2.sln`
- Session mode: read-only analysis, no app code changes yet

## 10. Follow-up progress

### 2026-06-29T08:33:00+07:00 - Dashboard pending folder review UI
- Reworked `AI_Study_Hub_v2/Components/Pages/Dashboard/DocumentDashboard.razor` from the old "My Documents" summary into a moderation-style `Review Pending Folders` page.
- New queue behavior:
  - reads real pending data from `community_reports` joined with `folders` and `documents`
  - groups multiple pending reports by folder
  - shows subject, semester, shared date, latest reason, and queue stats
  - adds `Approve`, `Reject`, and search/view actions per row
- Decision mapping used without backend code changes:
  - `Approve` -> marks pending reports as `Dismissed`
  - `Reject` -> marks pending reports as `Resolved` and unshares the folder if it is public
- Added `AI_Study_Hub_v2/Components/Pages/Dashboard/FolderReviewDetail.razor` as an analyst-style detail page for a single folder review with document list, metadata snapshot, and report timeline.
- Updated `AI_Study_Hub_v2/Components/Layout/DashboardLayout.razor` so the `Documents` sidebar item stays active on nested routes like `/dashboard/documents/review/{id}`.
- Verification:
  - `dotnet build "AI_Study_Hub_v2\\AI_Study_Hub_v2.sln" --nologo --no-restore`
  - Main app project compiled successfully to `AI_Study_Hub_v2\\bin\\Debug\\net8.0\\AI_Study_Hub_v2.dll`
  - Overall solution still fails because the test project cannot reach NuGet in this environment (`NU1301` against `https://api.nuget.org/v3/index.json`)

### 2026-06-29T09:02:00+07:00 - Dashboard/documents source corrected to My Folders
- Updated the dashboard review flow after user clarification that `/dashboard/documents` must use the same folder source as `/documents` -> `My Folders`.
- `AI_Study_Hub_v2/Components/Pages/Dashboard/DocumentDashboard.razor` now:
  - queries current-user folders instead of `community_reports`
  - mirrors the `My Folders` ownership scope (`folder.UserId == Session.CurrentUser.Id`)
  - orders pending/private folders first, then approved/public folders
  - maps review state to the existing share flag:
    - pending = `IsShared == false`
    - approved = `IsShared == true`
  - `Approve` shares the folder publicly
  - `Reject` moves the folder back to private
- `AI_Study_Hub_v2/Components/Pages/Dashboard/FolderReviewDetail.razor` was aligned to the same My Folders source and now reviews a single owned folder with the same approve/reject meaning.
- Verification attempt:
  - solution build still reports external restore failure for test project (`NU1301` / blocked NuGet)
  - app project output copy also hit a local file lock on `AI_Study_Hub_v2.exe` because a running dev instance is holding the binary (`MSB3026`)

### 2026-06-29T09:21:00+07:00 - Analytics wired from dashboard/documents magnifier
- Updated `AI_Study_Hub_v2/Components/Pages/Dashboard/DocumentDashboard.razor` so the magnifier action now navigates to `/dashboard/analytics?folderId={id}`.
- Evenly distributed the 6 table columns on `/dashboard/documents` using a fixed-layout table plus an equal-width `colgroup`.
- Reworked `AI_Study_Hub_v2/Components/Pages/Dashboard/AnalyticsDashboard.razor` into a folder-focused review dashboard:
  - reads the selected owned folder via query string
  - shows folder title + breadcrumb-style context
  - renders 4 top stat cards, a progress chart, a common-issues panel, and a document review table similar to the provided mockup
  - computes display-only metrics from existing folder/document fields without backend changes
- Updated `AI_Study_Hub_v2/Components/Layout/DashboardLayout.razor` to keep sidebar active state stable even when dashboard routes include query strings.
- Removed the no-longer-used `FolderReviewDetail.razor` route after shifting the review flow to analytics.
- Verification:
  - `dotnet build "AI_Study_Hub_v2\\AI_Study_Hub_v2.csproj" --nologo --no-restore /p:UseAppHost=false`
  - fixed one Razor parsing issue in the new SVG chart section during verification
  - remaining build blocker after that was a local file lock on `AI_Study_Hub_v2.dll` from a running app instance (`MSB3026`), not a compile error in the edited dashboard files
