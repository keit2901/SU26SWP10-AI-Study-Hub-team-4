# _CURRENT_SESSION — Sprint1 D5/D6 kickoff

**Started:** 2026-05-26T09:41Z
**Agent:** OpenCode (kr/claude-opus-4.7)
**Goal:** Mở session 14 sau khi file 13 đóng sạch (commit `679b0d4`). Verify state, rồi vào D5 (backend tests SCRUM-28) hoặc D6 (demo polish + folder picker) — theo Kiệt chốt.
**Status:** CLOSING
**Closed:** 2026-05-26T09:58Z

---

## 0. Context loaded

- [x] `rule.md` (read 2026-05-26T09:40Z)
- [x] `13_Session_2026-05-26_Sprint1_D4_Handoff.md` (canonical close, 09:38Z)
- [x] `02_Resume_Pack.md` §0-§4 + §11-§14 (state hiện tại sau D1-D4)
- [ ] `07_Phase2_Document_RAG_Plan.md` — load nếu vào D6 cần spec folder picker hoặc Sprint 2 prep
- [ ] File 11/12 — skip, file 13 + Resume Pack đã supersede

## 1. Verified state at start

**2026-05-26T09:43Z verify result: STATE OK với 1 finding + 1 blocker.**

```
Daemon:        29.4.3 ✓
Containers:    13 healthy (12 supabase + 1 legacy aistudyhub-db; pooler restart loop ignored)
Ports:         5432 ✓, 8000 ✓, 5240 ✓ (BACKEND RUNNING — unexpected)
Backend PID:   24024 AI_Study_Hub_v2.exe, started 2026-05-26 16:07:09 local (~09:07Z) → PRE-D4 BUILD
Git:           679b0d4 (clean code-side; only _CURRENT_SESSION.md untracked)
DB tables:     6/6 ✓ (__EFMigrationsHistory, roles, users, folders, documents, document_chunks)
Migrations:    2/2 ✓ (InitialSupabaseAuth, AddDocumentSchema)
Doc/chunk/folder rows: 0/0/0 (clean — last cleared D2 smoke 04:19Z)
Bucket:        documents private 50MB ✓ — MIME drift (see finding below)
Secrets:       6/6 ✓
Admin seed:    admin@aistudyhub.local ✓
Build:         BLOCKED — apphost.exe locked by PID 24024
Tests:         Not run (build prereq)
```

**Finding F1: Bucket MIME drift vs Resume Pack §14.**
- Actual (queried Storage REST `/bucket/documents`):
  `application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document,
   application/vnd.openxmlformats-officedocument.presentationml.presentation,
   application/msword, application/vnd.ms-powerpoint`
  → **pdf, docx, pptx, doc, ppt**
- Resume Pack §14 + §3.3 ghi: **pdf, doc, docx, txt, md**.
- Tức txt+md reject, pptx+ppt accept. Drift này có từ D1 bucket-create (file 11 hoặc earlier — không phải D4).
- Impact: D3 `DocumentUpload.razor` MIME whitelist phải khớp bucket policy để UX không lệch (Storage reject sau khi user click upload). Cần kiểm tra cụ thể trong file D3.

**Blocker B1: Backend PID 24024 chạy code pre-D4 + locks build output.**
- Started 09:07Z (Plan step 4 wait window file 13). D4 commit 09:34Z không có trong process này.
- Nếu Kiệt browser-test D4: route `/documents` + `/documents/{id}` chưa serve được tới khi restart.
- Nếu Kiệt cần build/test (D5 prep): phải stop PID 24024 trước.

## 2. Plan

1. Tạo file live (đã xong @09:41Z).
2. Verify daemon + containers + ports + DB tables + migrations + bucket + secrets + admin seed + build + tests + **git status** (mở rộng theo R2 file 13).
3. Báo Kiệt state + đề xuất hướng:
   - **D5** — backend tests `DocumentService` (NUnit + Moq + EF InMemory) + `DocumentsController` (WebApplicationFactory). SCRUM-28. Estimate 2-3h. (Recommended — củng cố trước khi UI polish.)
   - **D6** — demo polish + folder picker (cần backend Folders API trước → FoldersController + IFolderService + FolderApiClient + dropdown).
   - **Manual UI smoke D3+D4** — Kiệt browser test trước, gating cho D5/D6 closeout.
   - **Đóng session** — nếu Kiệt chỉ muốn handoff cho session sau.
4. Sau khi Kiệt chốt → load plan tương ứng (`07_Phase2_Document_RAG_Plan.md` nếu cần) + bắt đầu code.

## 3. Progress log (append-only, newest cuối)

### 2026-05-26T09:41Z — Session opened
- Created this file per rule.md §6.
- Context load: rule.md + file 13 + Resume Pack §0-§4 + §11-§14. Skip file 11/12 (đã supersede).
- Git state pre-verify: HEAD `679b0d4 docs(session): close 13 …`, working tree clean code-side, no `_CURRENT_SESSION.md` orphan.
- Next: chạy verify script Resume Pack §11 (mở rộng git status check).

### 2026-05-26T09:43Z — Verify PASS với 1 finding + 1 blocker
- 9/10 check green (daemon, containers, ports, git, DB tables, migrations, secrets, admin seed, doc rows = 0).
- F1 (drift): Bucket MIME = `pdf,docx,pptx,doc,ppt` thay vì Resume Pack §14 ghi `pdf,doc,docx,txt,md`. Cần check vs `DocumentUpload.razor` MimeWhitelist.
- B1 (blocker): Backend PID 24024 chạy code pre-D4 (started 09:07Z, D4 = 09:34Z) + locks `bin\Debug\net8.0\AI_Study_Hub_v2.exe` → `dotnet build` fail (MSB3027). Nếu Kiệt cần build/test thì phải kill trước.
- Awaiting Kiệt quyết: D5 / D6 / smoke / close + có kill backend không.

### 2026-05-26T09:44Z — F1 root cause: docs drift only, code 3-way consistent
- `DocumentService.cs:27-34` AllowedMimeTypes: pdf, docx, pptx, doc, ppt (5 mime).
- `DocumentApiClient.cs:20-27` AllowedMimeTypes: same 5 mime, comment "kept in sync with DocumentService.AllowedMimeTypes".
- `DocumentUpload.razor:98 AcceptAttr` + `:155 IsAllowedMime` extension fallback: `.pdf, .doc, .docx, .ppt, .pptx`.
- Bucket Storage REST: pdf, docx, pptx, doc, ppt — match.
- → Code/storage 4-way consistent. Resume Pack §14 + §3.3 ghi `pdf,doc,docx,txt,md` là **docs drift từ pre-D2** (likely copy nhầm khi tạo bucket spec). Fix khi close session: edit 2 dòng Resume Pack `pdf, doc, docx, txt, md` → `pdf, docx, pptx, doc, ppt`.
- F1 status: **non-blocking docs-only**. Không cần fix code.

### 2026-05-26T09:46Z — D5 plan locked, prep done
- B1 cleared: killed PID 24024 → port 5240 free → `dotnet build sln` 0/0 in 3.5s.
- Test project structure: `AI_Study_Hub_v2.Tests/` (csproj has NUnit 3.14, Moq 4.20.72, FluentAssertions 6.12.1, EF.InMemory 8.0.10, Mvc.Testing 8.0.10 — all packages D5 needs đã có).
- Existing tests: `Support/TestDb.cs` (Ignores Document+DocumentChunk+Folder), `Controllers/AuthControllerTests.cs` (21 tests, claim+exception pattern), `Services/SupabaseAuthServiceTests.cs` (17 tests, EF InMemory + Moq strict pattern). Total 38/38 pass historic.
- D5 plan locked:
  1. Extend `Support/TestDb.cs` — add `CreateInMemoryWithDocuments()` that Ignores only DocumentChunk (pgvector). Document/Folder have no Npgsql-only types beyond enum (.NET enum; PG type hint ignored by InMemory).
  2. `Services/DocumentServiceTests.cs` — ~18-20 tests covering Upload (happy + 7 error branches: user_not_found, user_inactive, empty_file, file_too_large, unsupported_media_type, folder_not_found, storage_upload_throws_no_row, persist_fail_cleanup), List (filters + ordering + isolation), GetById (happy + 404), Delete (happy + 404 + storage_fail_still_204).
  3. `Controllers/DocumentsControllerTests.cs` — ~7-9 tests: claim parsing (sub vs nameid), DocumentException mapping (404/403/415/413), missing_file 400, invalid sub claim 401, unexpected exception 500. Pattern same as AuthControllerTests (no WebApplicationFactory needed for unit-level controller).
- Acceptance: build 0/0, total tests ≥ 65 pass (38 existing + ~27 new), commit `test(documents): D5 ...`.
- Next: code TestDb extension + DocumentServiceTests file.

### 2026-05-26T09:48Z — DocumentService coverage GREEN (29 pass, 1 skip)
- `Support/TestDb.cs` extended: added `CreateInMemoryWithDocuments()` + `TestDocsDbContext` (Ignore only `DocumentChunk` for pgvector). 58 lines added.
- `Services/DocumentServiceTests.cs` written (30 tests, 558 lines):
  - Upload happy + filename sanitization (path traversal + unsafe chars stripped, year/userId/guid in path).
  - 4 error branches param test: 404 user_not_found, 403 user_inactive, 400 empty_file, 413 file_too_large.
  - 415 unsupported_media_type — `[TestCase]` matrix x4 (text/plain, image/png, application/zip, "").
  - Allowed mime acceptance — `[TestCase]` matrix x5 (full bucket policy: pdf/docx/pptx/doc/ppt). Locks F1 finding into a regression test.
  - Folder ownership: `folder_not_found` 404 + own-folder accept (FolderId persists).
  - Storage upload throws → bubble + zero rows (persist-cleanup branch hit upload-fail path; persist-throw cleanup is hard to reach without simulating SaveChanges fail — D5 leaves this as integration-only coverage, documented).
  - List: order DESC by CreatedAt + filter isolation + filter combos (subject case-insensitive, semester, folderId) + 404 user_not_found.
  - List Q-text branch: `[Ignore]` with explicit reason — `EF.Functions.ILike` has no InMemory translation; D2 live smoke covers it.
  - GetById: happy with signed URL, 404 not-owner, 404 user_not_found.
  - Delete: happy + storage delete called, 404 not-owner + row remains, 404 user_not_found, storage 503 still removes row + swallows.
- Run: 29 passed, 1 skipped (documented), 0 failed in 2.0s.
- Next: write `DocumentsControllerTests.cs`.

### 2026-05-26T09:50Z — Full D5 test suite GREEN (87 pass, 1 skip)
- `Controllers/DocumentsControllerTests.cs` written (20 tests, 365 lines):
  - Upload: happy 201 + claim forwarding (NameIdentifier path), sub fallback, missing file (null) + empty file → 400 missing_file, invalid sub claim → 401 missing_user_id, DocumentException matrix → status+code mapping (6 codes: user_not_found 404, user_inactive 403, file_too_large 413, unsupported_media_type 415, folder_not_found 404, upload_persist_failed 500), unexpected exception → 500 unexpected_error.
  - List: happy 200 + query passthrough, DocumentException → 404, unexpected → 500.
  - GetById: happy 200 + signed URL preserved, 404 document_not_found.
  - Delete: happy 204 NoContent, 404 document_not_found, unexpected 500.
- `dotnet build sln` 0/0 in 1.97s. `dotnet test sln` → **87 passed, 1 skipped, 0 failed in 1s**.
- Total D5 deliverable: +50 tests (30 service + 20 controller). Test count 38 → 88.
- Files changed:
  - `M AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Support/TestDb.cs` (+58 lines)
  - `?? AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/DocumentServiceTests.cs` (NEW, 558 lines)
  - `?? AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Controllers/DocumentsControllerTests.cs` (NEW, 365 lines)
- Next: commit `test(documents): D5 backend tests for DocumentService + DocumentsController (SCRUM-28) — 50 new tests, 87/88 pass + 1 documented skip`. Awaiting Kiệt confirm.

### 2026-05-26T09:54Z — D5 milestone commit
- D-2026-05-26-04: Kiệt confirm "Commit D5 ngay" at 09:54Z. Per rule §5.6 + D4 pattern, commit feat alone, hold session-doc to closeout.
- Staged 3 files (1 M Support/TestDb.cs, 2 A DocumentServiceTests + DocumentsControllerTests). `_CURRENT_SESSION.md` excluded per rule.
- `git commit -F <msg>` → `9dce4a0 test(documents): D5 backend tests for DocumentService + DocumentsController (SCRUM-28)`.
- Sprint 1 status: D1 ✅ D2 ✅ D3 ✅ D4 ✅ **D5 ✅** | D6 ⏳ (last item).
- Cleanup: removed temp commit message file at `C:\Users\pc\AppData\Local\Temp\opencode\d5-commit-msg.txt`.
- Next: Kiệt chốt — vào D6 (folder picker + demo polish, ~3-4h) hay đóng session 14 để bàn giao.

### 2026-05-26T09:58Z — CLOSING (Kiệt chọn close session 14 sau D5 win)
- Set Status: CLOSING.
- Refreshed `02_Resume_Pack.md`:
  - Header: date 2026-05-26 "Sprint 1 D1-D5", companion `14_*_D5_Handoff.md` (canonical close).
  - §3.1 tree → expanded test project section: `Support/TestDb.cs` `+CreateInMemoryWithDocuments()`; new `Services/DocumentServiceTests.cs` (30 tests detail) + `Controllers/DocumentsControllerTests.cs` (20 tests detail).
  - Footnote `Document backend test gap` → replaced with "D5 done — 50 tests + D2 live smoke gates integration; total 38 → 88".
  - §3.2 build: timestamp post-D5 09:53Z, 87/88 pass + 1 skip detail.
  - §3.3 storage bucket → fixed F1: MIME `pdf,doc,docx,txt,md` → `pdf,docx,pptx,doc,ppt` (4-way verified live 09:43Z).
  - §4 phases: row 12 D5 → ✅ commit `9dce4a0`; row 13 = D6 still pending; row 14 Sprint 2.
  - §4.5 D5 fact table (commit, deferred items, documented gap, harness extension).
  - §12 backlog: header "5/6 ✅", D5 row → done, D6 = next.
  - §13 files: split file 13 (D4) + new file 14 (D5 latest).
  - §14 quick facts: bucket MIME fix, build/tests timestamp + counts, git HEAD trail prepended `9dce4a0`.
- Next per rule §5.4-§5.6:
  1. Rename `_CURRENT_SESSION.md` → `14_Session_2026-05-26_Sprint1_D5_Handoff.md`.
  2. Stage `previous_session/14_*_Handoff.md` + `02_Resume_Pack.md`.
  3. Commit `docs(session): close 14 — D5 backend tests + Resume Pack refresh + F1 MIME drift fix`.
  4. No auto-push (rule §5.6).

## 4. Files changed this session

| Path | Change |
|---|---|
| `previous_session/_CURRENT_SESSION.md` | created → renamed `14_Session_2026-05-26_Sprint1_D5_Handoff.md` at close |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Support/TestDb.cs` | +58 lines — `CreateInMemoryWithDocuments()` + `TestDocsDbContext` (Ignore only DocumentChunk pgvector) — committed `9dce4a0` |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/DocumentServiceTests.cs` | NEW 558 lines — 30 NUnit tests (29 pass, 1 documented skip) — committed `9dce4a0` |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Controllers/DocumentsControllerTests.cs` | NEW 365 lines — 20 NUnit tests (claim parse + DocumentException matrix) — committed `9dce4a0` |
| `previous_session/02_Resume_Pack.md` | refreshed §header, §3.1 (tree + test footnote), §3.2 (build/tests post-D5), §3.3 (F1 MIME drift fix), §4 row 12 D5 done, §4.5 (NEW D5 fact table), §12 backlog (5/6 ✅), §13 files (file 14), §14 quick facts (bucket MIME + git HEAD `9dce4a0`) |

## 5. Commands run (chỉ những lệnh có side-effect)

- `Stop-Process -Id 24024 -Force` (09:43Z) — killed pre-D4 backend to unlock build
- `dotnet build sln` → 0/0 (09:46Z, 09:53Z)
- `dotnet test sln` → 87/88 pass + 1 skipped, 0 failed (09:53Z)
- `git add` 3 D5 files (09:54Z)
- `git commit -F <msg>` → `9dce4a0 test(documents): D5 backend tests …`

## 6. Decisions locked

- D-2026-05-26-03: **Session 14 = D5 backend tests (SCRUM-28).** Confirmed by Kiệt 2026-05-26T09:45Z.
  - Scope: NUnit tests cho `DocumentService` + `DocumentsController` unit-level (claim parsing + DocumentException mapping; integration via WebApplicationFactory deferred — D2 live smoke covers integration surface).
  - Pre-step: kill PID 24024 để unlock build output.
  - Acceptance: build 0/0, all tests pass (38 existing + new D5).
- D-2026-05-26-04: **Commit D5 ngay (no closeout coupling).** Confirmed by Kiệt 2026-05-26T09:54Z.
  - Rationale: same pattern as D4 (`50a8122`) — feat/test alone, session-doc waits to closeout commit.
- D-2026-05-26-05: **Đóng session 14 sau D5 win.** Confirmed by Kiệt 2026-05-26T09:58Z.
  - Rationale: D5 standalone milestone; D6 (folder picker + demo polish, 3-4h estimate) tốt hơn cho session sau với fresh context. Folder picker need backend Folders API (chưa có).

## 7. Open questions / risks

- ~~Q1: D5 hay D6 trước?~~ **Resolved 09:45Z** D-2026-05-26-03: D5 first (test coverage gate before UI polish).
- ~~Q2 (carried): Manual browser smoke D3+D4~~ **Still deferred** — Kiệt tự test khi tiện. Nếu fail mở entry `POST-UI-SMOKE` ở session 15+.
- ~~Q3 (carried): Folder picker spec~~ **Carried to D6** — block bởi backend Folders API; spec chốt khi vào D6.
- R1 (carried): Docker daemon long-pause shutdown. Cold-start gotcha file 12 §1 vẫn áp dụng.
- R2 (carried, partially mitigated): Drift từ agent khác — session 14 verify đã include `git status --porcelain` check (xem 09:43Z entry, không phát hiện drift mới sau file 13 close).
- R3 (new): D5 documented gap — `EF.Functions.ILike` Q-text branch chỉ chạy được trên Postgres thật. D2 live smoke + bucket smoke đã cover; nếu RAG chunking thay đổi query shape (Sprint 2) cần re-validate.
- R4 (new, low): Backend PID 24024 đã kill; cần restart trước khi Kiệt browser-test (`run-v2.cmd` tại §5.1 Resume Pack).

## 8. Next step (nếu pause/crash now)

**D5 đã commit `9dce4a0`. Sprint 1 status: D1-D5 ✅, D6 ⏳ (last item).**

Đang ở step closeout. Resume command để tiếp tục close hoặc verify:
```powershell
cd D:\FPT\summer2026\SWP391
git rev-parse HEAD                       # phải == 9dce4a0 (sau D5) hoặc commit mới hơn nếu close-out đã commit
git status --short                       # nếu thấy 14_*_Handoff.md untracked → cần stage + commit close-out
git log --oneline -3
```

Session 15 mở: theo rule §6 → đọc `02_Resume_Pack.md` § header + §11 verify + đọc file `14_*_D5_Handoff.md` (file này) → Section 0 + Section 9 đủ pickup. Rồi Kiệt chốt:
- **D6** — demo polish + folder picker (cần FoldersController + IFolderService + FolderApiClient + dropdown). Estimate 3-4h. Last Sprint 1 item.
- **Manual UI smoke D3+D4** — Kiệt browser test, gating cho D6 closeout.
- **Sprint 2 prep** — RAG planning (xem 07_Phase2_Document_RAG_Plan.md).

## 9. Quick Facts (snapshot)

```
Containers:    13 running (12 supabase + 1 legacy aistudyhub-db; pooler restart loop ignored)
DB:            postgres @ localhost:5432, 6 public tables, 2 migrations applied
Bucket:        documents (private, 50MB, 5 MIME: pdf/docx/pptx/doc/ppt — 4-way verified)
Backend:       STOPPED @ localhost:5240 (PID 24024 killed at 09:43Z; needs restart before browser test)
Tests:         87/88 pass + 1 documented skip, 0 failed (09:53Z this session)
               ├─ SmokeTests: 3
               ├─ SupabaseAuthServiceTests: 18
               ├─ AuthControllerTests: 17
               ├─ DocumentServiceTests: 30 (29 pass, 1 documented ILike skip)
               └─ DocumentsControllerTests: 20
Build:         0 warning, 0 error (09:53Z this session)
Git:           main @ 9dce4a0, working tree CLEAN code-side
               (only previous_session/_CURRENT_SESSION.md untracked, will be renamed at close)
Sprint 1:      D1 ✅ D2 ✅ D3 ✅ D4 ✅ D5 ✅  |  D6 ⏳ (last item)
D3+D4 UI:      manual browser smoke deferred to Kiệt (carried from sessions 12, 13)
Folder picker: deferred → D6 (needs Folders backend API first)
F1 fix:        Resume Pack §3.3 + §14 MIME drift corrected (txt+md → pptx+ppt)
```
