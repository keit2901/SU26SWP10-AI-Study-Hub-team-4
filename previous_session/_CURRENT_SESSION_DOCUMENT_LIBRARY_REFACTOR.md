# 📋 DocumentList Refactoring Plan - Final Script

**Status:** 🟡 Ready for Review  
**Created:** 2026-06-05  
**Target:** Replace `DocumentList.razor` (994 lines) with new Library UI design

---

## 🎯 EXECUTIVE SUMMARY

Replace the current `DocumentList.razor` page with a new layout inspired by Figma's `Dashboard.tsx` from `Library_UI_UX`. This is a **UI-only refactor** - all backend APIs remain unchanged.

### What Changes
- Layout: Sidebar + Header + Main content (from Figma design)
- Visual design: New welcome banner, stats cards, folder grid
- Components: All MudBlazor (no React/MUI)

### What Stays Same
- Backend endpoints ( Documents/Folders APIs)
- Data structures (DTOs, Entities)
- Features (upload, delete, folder create/rename, AI Chat integration)

---

## 📊 CURRENT VS NEW COMPARISON

### Current State (`DocumentList.razor`)
```
❌ 994 lines - monolithic component
❌ Table-centric layout (MudTable heavy)
❌ No welcome banner or stats
❌ Sidebar only navigation (no header)
❌ 404px sidebar width
❌ Complex nested filter logic
```

### New State (`DocumentLibrary.razor`)
```
✅ ~600-700 lines - modular components
✅ Grid + Sidebar + Header layout
✅ Welcome banner with 4 stats cards
✅ Header (56px) + Sidebar (240px, fixed)
✅ 4-column folder grid (visual cards)
✅ Simplified filter toolbar
✅ Right sidebar (300px) for recent folders
✅ Reusable components extracted
```

---

## 🗂️ FILE STRUCTURE

### Files to CREATE (5)
```
AI_Study_Hub_v2/
├── Components/
│   └── Pages/
│       └── DocumentLibrary.razor          (NEW - main page)
│   └── Shared/
│       ├── StatsCard.razor                (NEW - reusable)
│       ├── FolderCard.razor               (NEW - reusable)
│       └── RecentFolder.razor             (NEW - reusable)
└── wwwroot/css/pages/
    └── document-library.css               (NEW - component scoped)
```

### Files to MODIFY (2)
```
AI_Study_Hub_v2/
├── Components/
│   ├── Pages/
│   │   └── DocumentList.razor             (DELETE - old file)
│   └── Layout/
│       └── NavMenu.razor                  (UPDATE route link)
```

### Files to CHECK (no changes needed)
```
✅ AI_Study_Hub_v2/Services/DocumentApiClient.cs
✅ AI_Study_Hub_v2/Services/FolderApiClient.cs
✅ AI_Study_Hub_v2/Services/AiChatApiClient.cs
✅ AI_Study_Hub_v2/Dtos/DocumentDtos.cs
✅ AI_Study_Hub_v2/Dtos/FolderDtos.cs
```

---

## 🎨 LAYOUT SPECIFICATION

### Screen Dimensions (from Figma)
```
┌────────────────────────────────────────────────────────────────────────────┐
│  HEADER (fixed, 56px height)                                               │
│  [Logo] AI Study Hub | [Tabs] | [Search Bar] | [User Profile]           │
├────────────────────────────────────────────────────────────────────────────┤
│  SIDEBAR (fixed left, 240px width)                                         │
│  ┌────────────────────────────────────────────────────────────────────┐   │
│  │  AI Study Panel (chat button + tips)                              │   │
│  ├────────────────────────────────────────────────────────────────────┤   │
│  │  THƯ VIỆN HỌC TẬP                                                  │   │
│  │  [✓] Tất cả tài liệu                                              │   │
│  │  [ ] Tài liệu chưa phân loại                                      │   │
│  ├────────────────────────────────────────────────────────────────────┤   │
│  │  [Upload Link]                                                    │   │
│  └────────────────────────────────────────────────────────────────────┘   │
├────────────────────────────────────────────────────────────────────────────┤
│  MAIN CONTENT (ml: 240px, mt: 56px)                                        │
│  ┌────────────────────────────────────────────────────────────────────┐   │
│  │  WELCOME BANNER (gradient, 180px height)                          │   │
│  │  [Greeting Text], [Pro Badge]                                     │   │
│  │  [4 Stats Cards: Folders, Documents, RAG-Ready, Storage]         │   │
│  │  [New Folder] [Upload Document]                                   │   │
│  ├────────────────────────────────────────────────────────────────────┤   │
│  │  FOLDER GRID (4 columns, responsive)                              │   │
│  │  [Folder Card 1] [Folder Card 2] [Folder Card 3] [Folder Card 4] │   │
│  │  [Page Dots] [Create Folder]                                      │   │
│  ├────────────────────────────────────────────────────────────────────┤   │
│  │  TOOLBAR (filters + search)                                       │   │
│  │  [Subject] [Semester] [Folder] [Search] [Filter] [Clear]         │   │
│  │  [Active Filter Chips]                                            │   │
│  ├────────────────────────────────────────────────────────────────────┤   │
│  │  DOCUMENT TABLE (MudTable)                                        │   │
│  │  [Columns: Star, File, Subject, Semester, Folder, Size, Status, │   │
│  │         Date, Actions]                                             │   │
│  │  [Pagination: 10/25/50/100, Go to page]                           │   │
│  ├────────────────────────────────────────────────────────────────────┤   │
│  │  RIGHT SIDEBAR (300px, sticky)                                    │   │
│  │  [Thư Mục Gần Đây]                                                │   │
│  │  [Recent Folder Card 1]                                           │   │
│  │  [Recent Folder Card 2]                                           │   │
│  └────────────────────────────────────────────────────────────────────┘   │
└────────────���───────────────────────────────────────────────────────────────┘
```

### Spacing & Padding
```
MudStack Spacing="2" (16px)
MudPaper Class="pa-3" (12px padding)
MudTable Rows: py-1.5 (12px)
Folder Card: p-2 (16px)
Stats Card: p-2 (16px)
```

---

## 🔧 IMPLEMENTATION TASKS

### Phase 1: Component Extraction (45 minutes)
| Task | File | Status |
|------|------|--------|
| 1.1 Create `StatsCard.razor` | New | ⏳ |
| 1.2 Create `FolderCard.razor` | New | ⏳ |
| 1.3 Create `RecentFolder.razor` | New | ⏳ |
| 1.4 Create `document-library.css` | New | ⏳ |
| 1.5 Update `NavMenu.razor` | Modify | ⏳ |

**Acceptance:** Build succeeds, 0 errors/warnings

---

### Phase 2: Main Page Structure (90 minutes)
| Task | File | Status |
|------|------|--------|
| 2.1 Header section (fixed, 56px) | DocumentLibrary.razor | ⏳ |
| 2.2 Sidebar section (fixed, 240px) | DocumentLibrary.razor | ⏳ |
| 2.3 Welcome banner + stats grid | DocumentLibrary.razor | ⏳ |
| 2.4 Folder grid section (4 cols) | DocumentLibrary.razor | ⏳ |
| 2.5 Toolbar with filters | DocumentLibrary.razor | ⏳ |
| 2.6 Right sidebar (300px) | DocumentLibrary.razor | ⏳ |
| 2.7 Document table (all columns) | DocumentLibrary.razor | ⏳ |
| 2.8 Pagination controls | DocumentLibrary.razor | ⏳ |

**Acceptance:** Page renders with static data (mock folders/documents)

---

### Phase 3: Backend Integration (60 minutes)
| Task | File | Status |
|------|------|--------|
| 3.1 Add `LoadStatsAsync()` method | DocumentLibrary.razor | ⏳ |
| 3.2 Add `LoadFoldersAsync()` method | DocumentLibrary.razor | ⏳ |
| 3.3 Add `LoadDocumentsAsync()` method | DocumentLibrary.razor | ⏳ |
| 3.4 Add `LoadRecentFoldersAsync()` method | DocumentLibrary.razor | ⏳ |
| 3.5 Wire up `OnInitializedAsync()` | DocumentLibrary.razor | ⏳ |

**Acceptance:** Live data displays from API

---

### Phase 4: Interactive Features (90 minutes)
| Task | File | Status |
|------|------|--------|
| 4.1 Favorites toggle (star icon) | DocumentLibrary.razor | ⏳ |
| 4.2 Folder create (modal/dialog) | DocumentLibrary.razor | ⏳ |
| 4.3 Folder rename (inline edit) | DocumentLibrary.razor | ⏳ |
| 4.4 Folder delete (confirm dialog) | DocumentLibrary.razor | ⏳ |
| 4.5 Document delete (confirm dialog) | DocumentLibrary.razor | ⏳ |
| 4.6 Sort by Name/Date/Status | DocumentLibrary.razor | ⏳ |
| 4.7 Clear filters button | DocumentLibrary.razor | ⏳ |
| 4.8 Open AI Chat for folder | DocumentLibrary.razor | ⏳ |
| 4.9 Recent folder click → AI Chat | DocumentLibrary.razor | ⏳ |

**Acceptance:** All CRUD operations work with API

---

### Phase 5: Cleanup & Testing (30 minutes)
| Task | File | Status |
|------|------|--------|
| 5.1 Delete `DocumentList.razor` | Delete | ⏳ |
| 5.2 Update route references | NavMenu.razor | ⏳ |
| 5.3 Build verification | Build | ⏳ |
| 5.4 Test verification | Test | ⏳ |
| 5.5 Browser smoke test | Manual | ⏳ |

**Acceptance:** All 110+ tests pass, no errors

---

## 📋 FEATURE MAPPING

| Feature | Old Location | New Location | Status |
|---------|--------------|--------------|--------|
| Document upload | `/documents/upload` | `/documents/upload` | ✅ Same route |
| Document list | Table view | Grid + Table hybrid | ✅ Enhanced |
| Folder CRUD | Left sidebar | Sidebar + Folder Grid | ✅ Redesigned |
| AI Chat link | Folder cards | Folder cards + Right sidebar | ✅ Enhanced |
| Search/filter | Table header | Toolbar row | ✅ Improved |
| Favorites | Table column | Table column (star icon) | ✅ Same |
| Sort options | Table header | Toolbar buttons | ✅ Reorganized |
| Pagination | Table footer | Table footer | ✅ Same |
| Delete confirm | Dialog service | Dialog service | ✅ Same |

---

## 🎨 COLOR PALETTE

| Use Case | Code | MudBlazor Color |
|----------|------|-----------------|
| Primary (indigo) | `#6366f1` | `Color.Primary` |
| Success (emerald) | `#10b981` | `Color.Success` |
| Warning (amber) | `#f59e0b` | `Color.Warning` |
| Info (cyan) | `#06b6d4` | `Color.Info` |
| Text primary | `#1e293b` | `Color.Default` |
| Text secondary | `#64748b` | `Color.Secondary` |
| Background | `#f8f9fc` | `Color.Default` bg |
| Paper/Card | `#ffffff` | `Color.Default` paper |

---

## 📁 NEW FILE DETAILS

### `DocumentLibrary.razor` (Main Page)
**Expected size:** ~650 lines  
**Structure:**
```razor
@page "/documents"  ← Keep same route!

<MudAppBar Fixed="true" ...>
    <!-- Header content -->
</MudAppBar>

<MudDrawer @bind-Open="@_sidebarOpen" ...>
    <!-- Sidebar content -->
</MudDrawer>

<MudContainer Class="mt-5 ml-240px">
    <MudPaper Class="mb-4" Elevation="0">
        <!-- Welcome banner + stats -->
    </MudPaper>
    
    <MudPaper Class="mb-4" Elevation="0">
        <!-- Folder grid -->
    </MudPaper>
    
    <MudPaper Class="mb-4" Elevation="0">
        <!-- Toolbar -->
    </MudPaper>
    
    <MudPaper Elevation="0">
        <!-- Document table -->
    </MudPaper>
</MudContainer>

<!-- Right sidebar overlay (MudDrawer right side) -->
<MudDrawer Anchor="Anchor.Right" ...>
    <!-- Recent folders -->
</MudDrawer>
```

---

## 🔌 API ENDPOINTS (NO CHANGES NEEDED)

### Existing Endpoints (Already Working)
```
GET  /api/documents           → DocumentDto[]
GET  /api/documents/{id}      → DocumentDto
POST /api/documents/upload    → DocumentDto (201)
DELETE /api/documents/{id}    → 204
GET  /api/folders             → FolderDto[]
POST /api/folders             → FolderDto (201)
PUT  /api/folders/{id}        → FolderDto
DELETE /api/folders/{id}      → 204
GET  /api/ai/chat/ask         → ChatResponse (RAG)
```

### New Queries (Optional Optimization)
```
GET /api/documents/stats      → StatsDto (folders, ready, processing, size)
GET /api/folders/recent       → FolderDto[] (last 8 accessed)
```
**Note:** If these don't exist, compute on frontend with LINQ

---

## 🧪 TESTING CHECKLIST

### Build Verification
```bash
dotnet build AI_Study_Hub_v2/AI_Study_Hub_v2.sln
# Expected: Build succeeded, 0 warnings, 0 errors
```

### Test Verification
```bash
dotnet test AI_Study_Hub_v2/AI_Study_Hub_v2.sln
# Expected: ≥110 passed, 0 failed, 1 skipped
```

### Browser Smoke Test
- [ ] Login as admin
- [ ] Navigate to `/documents`
- [ ] Verify header visible (56px)
- [ ] Verify sidebar visible (240px)
- [ ] Verify welcome banner with stats
- [ ] Verify 4-column folder grid
- [ ] Create folder → appears in grid
- [ ] Upload document → appears in table
- [ ] Delete folder → confirms, removes
- [ ] Delete document → confirms, removes
- [ ] Click folder → opens AI Chat
- [ ] Sort by Name/Date → reorders
- [ ] Filter by Subject → filters
- [ ] Clear filters → resets
- [ ] Responsive: grid wraps at ~1200px

---

## ⚠️ RISKS & MITIGATIONS

| Risk | Impact | Mitigation |
|------|--------|------------|
| Route conflict `/documents` | HIGH | Keep same route, verify no other pages use it |
| CSS class collision | LOW | Use component-scoped CSS, namespace classes |
| API response format mismatch | MEDIUM | Add try-catch, show error message |
| State management break | MEDIUM | Reuse `AuthSessionState`, avoid duplicate state |
| Build cache issues | LOW | Run `dotnet clean` before `dotnet build` |

---

## 📝 DECISIONS REQUIRED FROM YOU

### Design Decisions
- [ ] **Q1:** Keep right sidebar (Recent Folders) visible?  
  Options: Always visible / Collapsible / Hidden
  
- [ ] **Q2:** Keep favorites system (star column)?  
  Options: Yes / No
  
- [ ] **Q3:** Keep folder inline rename?  
  Options: Yes / Remove (rename via dialog only)

### Technical Decisions
- [ ] **Q4:** Maintain backward compatibility with `/documents` route?  
  Options: Yes (current route) / Change to `/document-library`
  
- [ ] **Q5:** Add new API endpoints for `stats` and `recent folders`?  
  Options: Yes (backend work) / No (compute frontend)

### Timeline Decisions
- [ ] **Q6:** How to handle old `DocumentList.razor`?  
  Options: Delete immediately / Keep as backup (1 week)

---

## ✅ SUCCESS CRITERIA

Before merging, ALL of these must be true:
- [ ] `dotnet build` → 0 errors, 0 warnings
- [ ] `dotnet test` → ≥110 tests pass, 0 failed
- [ ] `/documents` page renders without errors
- [ ] Welcome banner shows (4 stats cards)
- [ ] Folder grid shows (4 columns)
- [ ] Document table shows (all columns)
- [ ] Sidebar visible (240px)
- [ ] Header visible (56px)
- [ ] Create folder works
- [ ] Upload document works
- [ ] Delete folder works (with confirm)
- [ ] Delete document works (with confirm)
- [ ] AI Chat link works (both places)
- [ ] Sort works (Name/Date/Status)
- [ ] Filter works (Subject/Semester)
- [ ] Clear filters works
- [ ] Responsive: grid wraps at ~1200px
- [ ] No console errors
- [ ] No network 4xx/5xx errors
- [ ] Old `DocumentList.razor` deleted

---

## 🚀 IMPLEMENTATION START COMMANDS

### Before Starting (Pre-flight)
```powershell
# 1. Verify git state
cd D:\Project\AI_Study_Hub\SU26SWP10-AI-Study-Hub-team-4\SU26SWP10-AI-Study-Hub-team-4
git status --short
git log --oneline -5

# 2. Verify backend running
docker compose -f infra\supabase\docker-compose.yml ps

# 3. Verify build clean (before changes)
dotnet build AI_Study_Hub_v2/AI_Study_Hub_v2.sln
dotnet test AI_Study_Hub_v2/AI_Study_Hub_v2.sln
```

### After Implementation
```powershell
# 1. Build new code
dotnet build AI_Study_Hub_v2/AI_Study_Hub_v2.sln

# 2. Run tests
dotnet test AI_Study_Hub_v2/AI_Study_Hub_v2.sln

# 3. Start app for manual testing
cd AI_Study_Hub_v2
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --urls http://localhost:5240

# 4. Open browser
# http://localhost:5240/login
```

---

## 📆 TIME ESTIMATE

| Phase | Estimated Time |
|-------|----------------|
| Phase 1: Components | 45 min |
| Phase 2: Structure | 90 min |
| Phase 3: Backend | 60 min |
| Phase 4: Features | 90 min |
| Phase 5: Testing | 30 min |
| **TOTAL** | **5 hours 15 minutes** |

**Buffer:** +30 min for unexpected issues  
**Total with buffer:** **6 hours**

---

## 🔄 ROLLBACK PLAN

If implementation fails at any point:

```powershell
# Option 1: Git rollback (if committed)
git log --oneline -10  # Find safe commit
git reset --hard <commit-hash>

# Option 2: Manual undo (if not committed)
# Delete new files:
Remove-Item "AI_Study_Hub_v2/Components/Pages/DocumentLibrary.razor"
Remove-Item "AI_Study_Hub_v2/Components/Shared/StatsCard.razor"
Remove-Item "AI_Study_Hub_v2/Components/Shared/FolderCard.razor"
Remove-Item "AI_Study_Hub_v2/Components/Shared/RecentFolder.razor"
Remove-Item "AI_Study_Hub_v2/wwwroot/css/pages/document-library.css"

# Restore old file (if renamed, not deleted)
git checkout HEAD -- "AI_Study_Hub_v2/Components/Pages/DocumentList.razor"

# Clean rebuild
dotnet clean AI_Study_Hub_v2/AI_Study_Hub_v2.sln
dotnet build AI_Study_Hub_v2/AI_Study_Hub_v2.sln
```

---

## 📞 SUPPORT RESOURCES

### Related Files (For Reference)
- `Library_UI_UX/src/app/components/Dashboard.tsx` (Figma design)
- `Library_UI_UX/src/app/components/FolderGrid.tsx` (Folder cards)
- `Library_UI_UX/src/app/components/Sidebar.tsx` (Sidebar layout)
- `Library_UI_UX/src/app/components/Header.tsx` (Header layout)
- `Library_UI_UX/src/app/components/ui/*.tsx` (MUI components → MudBlazor)

### Existing Code (Pattern Reference)
- `Components/Pages/DocumentUpload.razor` (File upload pattern)
- `Components/Pages/DocumentDetail.razor` (Delete confirm pattern)
- `Services/DocumentApiClient.cs` (API pattern)
- `Services/AuthSessionState.cs` (State management)

---

## ✅ FINAL APPROVAL

**This plan is ready for:**
- [ ] Technical review
- [ ] Design review
- [ ] Timeline approval
- [ ] Resource allocation

**Decision:**  
Approve? **Yes** / **No** / **With modifications**

**If yes:** I will begin Phase 1 implementation immediately.  
**If no:** Please specify modifications needed.

---

*Document generated: 2026-06-05*  
*Next step: Awaiting your confirmation to proceed*
