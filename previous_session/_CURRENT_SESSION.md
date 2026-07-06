# Current Session — 2026-06-29

## Completed

### 1. Branch triage & standardisation
- Analysed 4 branches: `main`, `sprint2/upload-improve-and-ai-chat`, `testing`, `sprint2/integration`
- `main` = 13 commits ahead of `testing`, `testing` = 5 ahead of `sprint2/upload-improve-and-ai-chat`
- **Adopted `main` as standard branch** — most complete (sprint3 backend, community report, reCAPTCHA)
- Switched from `testing` → `main` as primary workspace

### 2. Database migration fix
- `dotnet run` failed with `42P07: relation "community_reports" already exists`
- Root cause: `__EFMigrationsHistory` was missing 2 migration records (`20260614081530_AddSprint3KiBackend`, `20260624025917_AddCommunityReport`) while having a stale record (`20260628111243_MergeSprint3Changes`)
- Fixed via `docker exec supabase-db psql`: INSERT 2 missing migrations, DELETE stale migration
- Result: "No migrations were applied. The database is already up to date."

### 3. DocumentList deleted
- Route conflict `'documents'` between `DocumentList.razor` and `DocumentLibrary.razor`
- Deleted `DocumentList.razor` + `DocumentList.razor.css` — DocumentLibrary is the replacement

### 4. IRR Phase 1 Readiness (review & document)
- Reviewed `IRR_Phase1_Readiness.md` from prior session
- Confirmed all 6 validations **pass with conditions**:
  - Rollback PASS
  - Benchmark PASS
  - Token Distribution PASS w/ critical finding (ChunkSizeChars=700 not applied)
  - Storage Portability PASS
  - Backend Comparison PASS
  - Provider Abstraction PASS
- Documented DB schema changes (sprint3 added `Quiz`, `CommunityReport`, updated `DocumentChunks`)
- Conditions for Phase 1 documented: Ollama Dockerfile, DI wiring, ChunkSizeChars=700, re-ingest

### 5. Page restoration (4 pages — all resolved)
All 4 pages restored to `sprint2/upload-improve-and-ai-chat` baseline:

| Page | Status | Notes |
|------|--------|-------|
| **Home.razor + Home.razor.css** | ✅ Restored | sprint2 landing page (hero + bento grid + CTA + footer) |
| **Community.razor** | ✅ Restored | sprint2 version without report dialog/CommunityApiClient |
| **DocumentUpload.razor** | ✅ No diff needed | Identical on both branches |
| **AiChat.razor + AiChat.razor.css** | ✅ Restored | sprint2 version (no quiz modal inline, no topic cloud/tags). Merge conflict markers `<<<<<<< HEAD` present in main version — now clean. |

App runs at `http://localhost:5240` — Blazor Server, 0 build errors, 0 migration errors, 7 pre-existing warnings.

## Important Context
- **Branch**: `main` at `f5e9c86`
- **Database**: 9 migrations in `__EFMigrationsHistory`, all matched. No pending migrations.
- **Stash**: `stash@{0}` on `sprint2/upload-improve-and-ai-chat` (session config), `stash@{1}` on `testing` (`setup.ps1`)
- **4 pages restored**: Home (landing ✅), Community (sprint2 ✅), Upload (no diff ✅), AiChat (sprint2 ✅)

## Remaining Concerns
- Phase 1 real embedding deployment (Ollama Dockerfile, DI, ChunkSizeChars=700, re-ingest) **not implemented** — conditions documented in IRR
- 7 build warnings are pre-existing
- `DocumentLibrary.razor` has unused `_foldersBusy` field (CS0414 warning)
- QuizService + DocumentDetail have possible null dereference warnings (CS8602)

### 6. feat/dashboard-ui merge — pull + verify + clean (2026-06-29)
- Pulled `feat/dashboard-ui` merge (PR #18 → reverted by PR #19 → re-reverted by PR #20)
- Current at `a8bb832` (revert-of-revert = dashboard-ui merge active)
- Verified folder-scoped analytics:
  - `FolderTable.razor` row click → `/dashboard/analytics?folderId=...` ✅
  - `AnalyticsDashboard.razor` reads `FolderId` from query, calls `GetUserAnalyticsAsync(userId, FolderId)` ✅
  - `DashboardService.GetUserAnalyticsAsync` filters query by `d.FolderId == folderId` ✅
  - "Inspect Documents" button → `/documents?folderId=...` ✅
  - `DocumentLibrary.razor` handles `folderId` query param in `OnParametersSet` ✅
- Cleaned:
  - Removed duplicate `@inject AppDbContext DbContext` in FolderDashboard.razor
  - Removed dead `FolderViewModel.FolderId` (nullable, unused)
  - Removed unused `_folderName` (CS0414) + duplicate `folderId` query param in AnalyticsDashboard.razor
- Build: **0 errors, 14 warnings (all pre-existing)**

## Handoff Notes for Next Session
- Next work: either start Phase 1 embedding deployment or new feature work
- If `dotnet run` has migration errors, check `__EFMigrationsHistory` matches all 9 migration names in code
- The `AGENTS.md` instructions reference `D:\FPT\summer2026\SWP391_parallel\s2_integration` as common integration worktree — that path may not exist on this machine
- Read this file + `rule.md` + `skill.md` for context
- All 4 pages were restored because the merged main had features from `testing` branch (quiz modal, report dialog, forum dashboard layout) that the user wanted to revert

### 7. Avatar + logout in DashboardLayout (2026-06-29)
- Added user initial avatar (purple circle) + logout button to DashboardLayout top bar, conditionally visible when authenticated
- Logout navigates to `/logout`

### 8. DocumentDashboard fixes (2026-06-29)
- **Inspect in folder view**: DashboardService.GetDocumentsByFolderAsync now calls GetDocumentSignedUrlAsync to bypass ownership check
- **Global pending shares**: GetPendingDocumentsAsync queries user-owned docs AND docs in PendingShare folders not owned by user. Added FolderName to PendingDocumentDto.
- **UX after approve/reject**: Row shows status badge (Pending=orange, Approved=green, Rejected=red) instead of action buttons. Filename click opens document (no separate button).
- **Analytics update fix**: GetAdminAnalyticsAsync no longer scoped to current user, works after approve/reject

### 9. Brand icon in moderator sidebar (2026-06-29)
- Added purple/cyan gradient "AI Study Hub" logo header above NavMenu in DashboardLayout.razor

### 10. Document review status tracking + folder locking (2026-06-29)
- **New `DocumentReviewStatus` enum**: None(0), Pending(1), Approved(2), Rejected(3) in Data/Entities/Document.cs
- **ReviewStatus column** on Document entity (default None via fluent config)
- **DocumentDto.ReviewStatus** exposed
- **DashboardService** maps ReviewStatus instead of hardcoding "Approved"; sets status on approve/reject
- **AnalyticsDashboard**: status badges colored by review status
- **Folder locking**: `_allDocumentsReviewed` + `_hasRejectedDocument` control button state:
  - Not all reviewed → lock icon
  - All reviewed + rejected doc → Approve disabled (greyed), only Reject active
  - All reviewed + no rejects → both active
- **Migration**: `20260629155132_AddDocumentReviewStatus` (adds ReviewStatus column)
- Build: **0 errors, 13 warnings** (all pre-existing). 4 QuizServiceTests failures are pre-existing (exception type, JSON casing).

### 11. Analytics global scope fix (2026-06-29)
- **Problem**: Global analytics (no folder selected) only showed documents from PendingShare folders — missed approved/rejected folders and orphans.
- **Fix**: Removed the `FolderStatus.PendingShare` filter in `GetAdminAnalyticsAsync` global branch. Now returns ALL documents across all folders (and orphans), computed by ReviewStatus.
- File changed: `Services/DashboardService.cs` line 338-341
