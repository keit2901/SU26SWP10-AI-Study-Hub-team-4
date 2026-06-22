# _CURRENT_SESSION — Upload Improvement + AiChat + Folder Sort

**Started:** 2026-06-17
**Agent:** OpenCode
**Goal:** Fix Supabase Storage upload errors, add multi-file upload, fix NavMenu dual-highlight, fix AiChat checkbox alignment, folder UpdatedAt tracking, pre-select workspace files
**Status:** CLOSING

---

## 0. Context loaded

- [x] `handoff_backend_2026-06-17.md` (copied to project `previous_session/`)
- [x] `rule.md`
- [x] `skill.md`
- [x] Previous session docs for Supabase Storage setup + bucket creation

## 1. Problems Solved

### P1: Supabase Storage upload — DNS resolution failure
**Error:** `"failed the initial dns/balancer resolve for 'storage' with: dns server error: 2 server failure"`

**Root cause:** `supabase-storage` container was completely missing. The Docker Compose project `aistudyhub-supabase` was started without `--profile phase2`, so all Phase 2 services (storage, imgproxy, realtime, vector, supavisor, functions) were never created.

**Fix:**
```powershell
docker compose -p aistudyhub-supabase --profile phase2 up -d
```
Reference: `07_Phase2_Document_RAG_Plan.md:247`, `01_Architecture_Reference.md:119`

### P2: Supabase Storage upload — "Bucket not found"
**Error after P1 fix:** `"Bucket not found"` — fresh storage container has empty database.

**Fix:** Created `documents` bucket via Storage REST API:
```powershell
POST /storage/v1/bucket
{ "name": "documents", "id": "documents", "public": false,
  "file_size_limit": 52428800,
  "allowed_mime_types": ["application/pdf", ...] }
```
Settings: private, 50MB, 5 MIME types (pdf/docx/pptx/doc/ppt).

### P3: Multi-file upload — user couldn't select multiple files
**Root cause:** `MudFileUpload<T>` has a coupling between generic type `T` and the HTML `multiple` attribute:
- `T="IBrowserFile"` → `multiple=false` → single file only
- `T="IEnumerable<IBrowserFile>"` → `multiple=true` but `OpenFilePickerAsync()` breaks (file picker doesn't open)

**Cycle:**
1. `T="IBrowserFile"` → picker works, but no multi-select
2. `T="IEnumerable<IBrowserFile>"` → multi-select works in theory, but `OpenFilePickerAsync()` fails silently
3. Reverting to `T="IBrowserFile"` fixed picker but broke multi-select again

**Fix:** Replaced `MudFileUpload<T>` entirely with Blazor's native `<InputFile>` component + `IJSRuntime` to trigger `.click()`:
- `InputFile` unconditionally supports `multiple` HTML attribute
- A small inline JS function `window.openFilePicker(id)` calls `document.getElementById(id).click()`
- Added to `App.razor` as inline `<script>`
- Removed `_uploader` field and `ClearAsync()` calls entirely

**Multi-file UX:**
- Drop zone shows scrollable file list with individual remove buttons + "Remove all"
- Upload button shows progress: `Uploading 3/5...`
- Success banner shows `N file(s) uploaded successfully, M failed.`
- Each file validated individually (size + MIME)
- All files share same metadata (Subject Code, Semester, Folder)
- Sequential upload via client loop (backend API is single-file only)

### P4: NavMenu dual-highlight (both Library and Upload active on `/documents/upload`)
**Root cause:** `NavLink` uses `NavLinkMatch.Prefix` by default. `/documents/upload` starts with `/documents`, so both Library (`href="documents"`) and Upload (`href="documents/upload"`) matched.

**Fix:** Replaced `NavLink` with `<a>` tags + manual `NavClass()` method:
```csharp
private string NavClass(string href)
{
    var rel = Nav.ToBaseRelativePath(Nav.Uri).TrimEnd('/');
    if (href == "documents")
        return (rel == "documents" || rel.StartsWith("documents/")) && !rel.StartsWith("documents/upload") ? "active" : "";
    if (href == "documents/upload")
        return rel == "documents/upload" ? "active" : "";
    return rel == href ? "active" : "";
}
```
Library activates on `/documents` or `/documents/{subpath}` but NOT `/documents/upload`.
Upload activates only on exactly `/documents/upload`.

### P5: AiChat checkbox checkmark misaligned (bottom-right)
**Root cause:** `MudIcon Size="Size.Small"` renders an SVG at `1.25rem` (~20px) font-size inside a `1.05rem` (~16.8px) container. The SVG overflows to the bottom-right.

**Fix:** Replaced MudIcon with a pure CSS checkmark (`::after` border trick):
- `.workspace-doc-check` now uses `display: flex` with `align-items/justify-content: center`
- `.workspace-doc-check-mark` uses rotated `border` (0.3rem × 0.5rem box with bottom+right border) — no SVG, no viewBox padding
- `overflow: hidden` on the container prevents any bleed
- Grid column widened from `1.1rem` → `1.25rem` to prevent clipping

### P6: Folder UpdatedAt not updated on document upload
**Problem:** When a document was uploaded to a folder, `Folder.UpdatedAt` was never updated. The "Recent Folders" section on the library page showed stale data.

**Fix:** In `DocumentService.cs:155-162`, before saving the document, if `request.FolderId` is set, the folder is loaded via `FindAsync` and its `UpdatedAt` is set to the current timestamp. This happens in the same `SaveChanges` transaction.

### P7: Library folder sort by name instead of recency
**Problem:** Folders in the library were sorted alphabetically by name. Recently updated folders should appear first (after favorites).

**Fix:**
- `FolderService.cs:28`: `.OrderBy(f => f.Name)` → `.OrderByDescending(f => f.IsFavorite).ThenByDescending(f => f.UpdatedAt)`
- `DocumentLibrary.razor`: All client-side re-sort calls updated to use `UpdatedAt` desc instead of `Name`, including after create, rename, toggle-favorite, toggle-share, and change-icon

### P8: Workspace files not pre-selected
**Problem:** When entering a folder workspace, no files were checked. Users had to manually tick each file they wanted to ask about.

**Fix:** `AiChat.razor` changes:
- New `SelectAllScopeDocuments()` helper — clears and re-populates `_selectedDocumentIds` with all `ScopeDocuments`
- `ApplyQueryScope()` and `OnWorkspaceFolderChanged()` both call `SelectAllScopeDocuments()` instead of the previous stale-pruning `RemoveWhere`
- `QueryDocumentId` case still selects only the specific linked document
- Users can uncheck any files they don't want to include

## 2. Files Changed

| Path | Change |
|---|---|
| `Services/DocumentService.cs` | New: folder `UpdatedAt` update on document upload (lines 155-162) |
| `Services/FolderService.cs` | Sort changed: `OrderBy(Name)` → `OrderByDesc(IsFavorite).ThenByDesc(UpdatedAt)` |
| `Components/Pages/DocumentUpload.razor` | Multi-file: `InputFile` + `IJSRuntime` replaces `MudFileUpload`; `_files` list; batch loop; progress |
| `Components/Pages/DocumentUpload.razor.css` | File list styles `.drop-zone-file-list`, `.drop-zone-file-item`, etc. |
| `Components/Pages/AiChat.razor` | New `SelectAllScopeDocuments()`; `ApplyQueryScope()`/`OnWorkspaceFolderChanged()` pre-select all; MudIcon→CSS checkmark |
| `Components/Pages/AiChat.razor.css` | Checkbox: `display: flex` centering, `overflow: hidden`, CSS checkmark via border rotation; grid column `1.1rem→1.25rem` |
| `Components/Pages/DocumentLibrary.razor` | All folder re-sort calls use `UpdatedAt` desc instead of `Name` |
| `Components/Layout/NavMenu.razor` | `NavLink` → `<a>` tags + `NavClass()` for manual active-state |
| `Components/App.razor` | Inline `<script>`: `window.openFilePicker=id=>document.getElementById(id).click()` |

## 3. Commands Run (side-effect)

- `Copy-Item handoff_backend_2026-06-17.md` to project `previous_session/`
- `docker compose -p aistudyhub-supabase up -d storage` — started missing storage container
- `Invoke-RestMethod POST /storage/v1/bucket` — created `documents` bucket
- `docker compose -p aistudyhub-supabase --profile phase2 up -d` — started all Phase 2 services (partial fail due to TLS timeout on realtime/vector/supavisor images)
- `dotnet build sln` — 0 errors each time

## 4. Decisions Locked

- **D-2026-06-17-01:** `MudFileUpload<T>` is unsuitable for multi-file in this MudBlazor version — replaced with `InputFile` + JS interop instead of fighting the generic-type coupling.
- **D-2026-06-17-02:** Backend upload API remains single-file; multi-file is handled client-side via sequential loop. Adding a batch endpoint is deferred.
- **D-2026-06-17-03:** `NavLink` replaced with `<a>` tags for precise active-state control — Blazor's prefix matching is too coarse for overlapping routes `/documents` vs `/documents/upload`.
- **D-2026-06-17-04:** AiChat pre-selects all scope documents by default; uncheck to exclude. `QueryDocumentId` still selects only the linked document.
- **D-2026-06-17-05:** Folder sort prioritizes recency (`UpdatedAt` desc) after favorites — library shows most recently updated folders first.

## 5. Quick Facts

```
Containers:    9 running (supabase-storage + supabase-imgproxy healthy)
               Phase 2 partial: realtime/vector/supavisor failed to pull (TLS timeout)
DB:            postgres @ localhost:5432
Bucket:        documents (private, 50MB, 5 MIME) — created this session
Backend:       stopped (was PID 7780, killed for rebuild)
Build:         0 error (clean)
Tests:         146/147 pass (1 documented skip)
Git:           working tree with uncommitted changes:
               - DocumentService.cs (+folder UpdatedAt)
               - FolderService.cs (+recency sort)
               - DocumentUpload.razor (+multi-file)
               - DocumentUpload.razor.css (+file list)
               - AiChat.razor (+pre-select +CSS checkmark)
               - AiChat.razor.css (+checkmark styles)
               - DocumentLibrary.razor (+recency sort)
               - NavMenu.razor (+NavClass)
               - App.razor (+openFilePicker script)
```

## 6. Next Steps

1. Restart backend and test multi-file upload end-to-end
2. If Phase 2 services were only partially started, retry `docker compose --profile phase2 up -d` when network is stable (realtime/vector/supavisor images failed TLS)
3. Subject code validation mismatch (client `/^[A-Z]{2,4}\d{3,4}$/` vs server `/^[A-Z]{3}[0-9]{3}$/`) — needs alignment
4. Verify workspace pre-select behavior: entering a folder should check all files, uncheck works individually
