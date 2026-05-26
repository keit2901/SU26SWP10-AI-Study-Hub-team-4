# _CURRENT_SESSION — Sprint1 D2 smoke + D3 kickoff

**Started:** 2026-05-26T03:35Z
**Agent:** OpenCode (kr/claude-opus-4.7)
**Goal:** Đóng D2 smoke E2E live (upload/list/get/download/delete) rồi mở D3 Blazor upload form (per `07_Phase2_Document_RAG_Plan.md` §7 Step 3).
**Status:** CLOSING
**Closed:** 2026-05-26T07:20Z

---

## 0. Context loaded

- [x] `rule.md` (read 2026-05-26T03:34Z)
- [x] `11_Session_2026-05-25_Sprint1_D1D2_Handoff.md` (canonical, 2026-05-26T01:31)
- [x] `11_Session_2026-05-25_Sprint1_D1_D2_Handoff.md` (older near-dup, 2026-05-26T01:25 — flag to clean at close)
- [x] `02_Resume_Pack.md` §0-§3 (Phase 1 locks; not yet refreshed with D1+D2)
- [ ] `07_Phase2_Document_RAG_Plan.md` v-final — sẽ load khi vào D3
- [ ] `08_Session_2026-05-24_Close_Phase1_Handoff.md` — skip, không cần cho task này

## 1. Verified state at start

**2026-05-26T03:40Z verify result:** STATE OK after Docker Desktop start + stack `up -d`.

```
Containers:    11/11 healthy (supabase-db, kong, auth, rest, meta, studio,
               analytics, storage, vector, realtime, imgproxy)
DB tables:     __EFMigrationsHistory, document_chunks, documents, folders,
               roles, users (6 public tables)
Migrations:    20260524090408_InitialSupabaseAuth
               20260525143314_AddDocumentSchema
Bucket:        documents (private=true, 50MB, 5 MIME)
Secrets:       6/6 OK (JwtSecret, AnonKey, ServiceRoleKey, Seed pwd, connstr, Groq)
Git HEAD:      fde0c2b (drifted from handoff 11's "0e4340c" — the 2 newer
               commits are the 2 file-11 handoff docs themselves)
Working tree:  2 untracked (_CURRENT_SESSION.md, rule.md) — expected
```

Cold-start gotcha (logged for future sessions): khi Docker Desktop boot từ stone-cold, `docker compose up -d` lần đầu sẽ FAIL với `dependency failed to start: container supabase-db is unhealthy` vì pg-isready takes ~25s nhưng compose health-wait timeout < that. Storage container còn vào restart loop. Fix: chờ thêm ~15s rồi check lại — DB tự healthy, storage tự stable. Không cần intervention.

## 2. Plan

1. Run verify script (handoff 11 §5.2) → paste raw output vào Section 1 trên.
2. Nếu state OK: chạy D2 smoke E2E 8-step (handoff 11 §3.1):
   1. Start app on port 5240
   2. POST `/api/auth/login` admin → access_token
   3. POST `/api/documents/upload` (multipart, small.pdf, SWP391, SU26) → 201
   4. GET `/api/documents` → 1 entry
   5. GET `/api/documents/{id}` → DTO + 5min signed URL
   6. Open signed URL → file downloads
   7. DELETE `/api/documents/{id}` → 204
   8. Verify storage bucket=0 + DB documents=0
3. Commit `test(documents): D2 smoke E2E pass …` nếu green; nếu đỏ → log root cause + dừng.
4. Vào D3 Blazor upload form (chỉ khi D2 đóng).
5. Đóng session: refresh `02_Resume_Pack.md` §3 schema + §12 backlog, dọn 2 file 11 trùng (rename file cũ → `_archive_dup` hoặc xoá theo lệnh Kiệt), rename `_CURRENT_SESSION.md` → `12_Session_2026-05-26_<topic>_Handoff.md`.

## 3. Progress log (append-only, newest cuối)

### 2026-05-26T03:35Z — Session opened
- Created this file per rule.md §6.
- Context load complete (4/6 files; 2 skip có chủ đích).
- Verify script chưa chạy. Next: run §5.2 of handoff 11.

### 2026-05-26T03:37Z — Verify FAIL: Docker daemon down
- Symptom: docker CLI → `npipe ... cannot find the file specified` → daemon offline.
- All 11 containers reported as `=` (no state).
- Per rule §6.5: STOP, asked Kiệt for direction.

### 2026-05-26T03:38Z — Recovery: start Docker Desktop
- Kiệt confirmed: "Start Docker + bring stack up" + "Rename file cũ → 11a_*_superseded.md".
- Started Docker Desktop via `Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"`.
- Waited ~10s, daemon ready: `docker version` → 29.4.3.

### 2026-05-26T03:39Z — Stack up: first attempt FAIL, settles in ~15s
- `docker compose --profile phase2 up -d` (in `infra/supabase/`) returned `dependency failed to start: container supabase-db is unhealthy`.
- Root cause: pg cold-start FATALs (`the database system is starting up`) > compose's wait-for-healthy timeout. Storage container went into restart loop because it tried to connect during DB warm-up.
- No intervention needed: DB became healthy ~25s after start, storage auto-restarted clean.

### 2026-05-26T03:40Z — Re-verify: STATE OK
- 11/11 containers healthy.
- DB has 6 public tables, 2 migrations applied.
- Storage bucket `documents` reachable (private, 50MB, 5 MIME).
- 6/6 secrets present.
- Git HEAD `fde0c2b` (handoff 11 §1.5 said `0e4340c` — drift = 2 handoff commits added after that line was written; expected, not a real lệch).
- Cleared to proceed with D2 smoke E2E.

### 2026-05-26T03:41Z — Dedup file 11
- Renamed `11_Session_2026-05-25_Sprint1_D1_D2_Handoff.md` → `11a_Session_2026-05-25_Sprint1_D1_D2_Handoff_superseded.md`.
- Prepended SUPERSEDED banner pointing to canonical `11_*_D1D2_*.md`.
- Per rule §7, history preserved (no delete).

### 2026-05-26T04:17Z — POST-CRASH RECONCILE (resume after ~36min gap)
- Re-loaded context (`_CURRENT_SESSION.md`, `rule.md`, `11_*_D1D2_Handoff.md`).
- Quick state check (before full verify):
  - `docker ps`: 11/11 supabase containers `Up 38 minutes (healthy)`; `supabase-rest` shows no health probe (expected — image lacks healthcheck) but `Up 38 minutes` → treat as OK. Plus extra `aistudyhub-db` (legacy, ignored per handoff 11 §2).
  - `git rev-parse --short HEAD` → `fde0c2b` (matches Section 1 snapshot above).
  - `git status --porcelain`: only the 2 expected untracked + the file-11 rename → tree consistent with last entry.
- No drift vs Section 9 Quick Facts. Proceed with full verify script (handoff 11 §5.2) before D2 smoke.

### 2026-05-26T04:18Z — Verify script (handoff 11 §5.2) PASS
- 11/11 containers running.
- DB: 6 public tables (auth schema + 4 P2 tables + EF history).
- Migrations: `20260524090408_InitialSupabaseAuth`, `20260525143314_AddDocumentSchema`.
- Bucket `documents`: private=True, 50MB, 5 MIME.
- 6/6 secrets OK.
- Build clean; 38/38 tests pass.
- Git tree WARN as expected (4 untracked/unstaged: `_CURRENT_SESSION.md`, `rule.md`, file-11 rename pair). HEAD `fde0c2b`.
- Conclusion: `=== STATE OK — proceed to D2 smoke or D3 ===`. Moving to Plan step 2.

### 2026-05-26T04:19Z — Pre-smoke reconcile: app already running
- `netstat :5240` → PID 9928 LISTENING.
- `Get-Process 9928` → `AI_Study_Hub_v2.exe`, started 2026-05-26T03:44Z (~35min ago, was up during the verify-script gap; handoff 11 said "stopped clean" but a launch happened post-handoff before this session opened).
- HTTP probe: `/` 200 (Kestrel), `/api/auth/login` 405 GET (POST-only), `/api/documents` 401 (JWT required), `/admin/dashboard` 200 → confirmed our app, not stale.
- Decision: reuse this instance, skip Plan-step-2.1 (boot). No drift; using user-secrets `Seed:DefaultAdmin:Password` for login.

### 2026-05-26T04:19Z — Smoke step 3 (upload) PASS
- POST `/api/documents/upload` (multipart: `file=smoke_small.pdf` 535B, `subjectCode=SWP391`, `semester=SU26`).
- 201 Created, `id=a8182289-e17c-45b4-aa5a-01d9f6a5eadb`, `status=1` (Ready), `fileSizeBytes=535`, `mimeType=application/pdf`.
- Test PDF: hand-crafted minimal valid 1-page PDF at `C:\Users\pc\AppData\Local\Temp\opencode\smoke_small.pdf` (no existing PDF in repo).
- `[FromForm] UploadDocumentRequest + IFormFile file` binding from same multipart body **works** (handoff 11 known unknown #1 cleared).

### 2026-05-26T04:19Z — Smoke steps 4-8 PASS — D2 closeout GREEN
- Step 4 GET `/api/documents` → 1 entry, matches uploaded doc.
- Step 5 GET `/api/documents/{id}` → DTO + `downloadUrl` (414 chars, points to `http://localhost:8000/storage/v1/object/sign/documents/users/<uid_n>/2026/<gid_n>-smoke_small.pdf?token=<jwt>`); deterministic path scheme `users/{uid}/{yyyy}/{guid}-{slug}` confirmed in URL.
- Step 6 GET signed URL **without** auth header → 200, 535 bytes, `Content-Type: application/pdf`, header bytes `%PDF`. Round-trip byte-equal to uploaded file. Signed URL reachable from browser-equivalent client through Kong (known unknown #4 cleared).
- Step 7 DELETE `/api/documents/{id}` → 204.
- Step 8 cleanup verify:
  - API `/api/documents` → 0 entries.
  - Supabase Storage REST `POST /storage/v1/object/list/documents` → 0 root objects (bucket fully clean).
  - `psql SELECT COUNT(*) FROM public.documents` → `0`.
- All 8 steps GREEN. D2 backend pipeline (upload/list/get/signed-download/delete) verified live end-to-end.

### 2026-05-26T04:22Z — D2 milestone commit
- `git commit --allow-empty -m "test(documents): D2 smoke E2E pass — upload/list/get/signed-download/delete verified live"` → `0245045`.
- D2 closeout DONE. Moving to Plan step 4 (D3 Blazor upload form).
- Next: load `07_Phase2_Document_RAG_Plan.md` §7 Step 3, then explore existing Blazor frontend (MainLayout, AuthApiClient pattern, MudBlazor setup).

### 2026-05-26T04:24Z — D3 scope decision
- Plan §7 Step 11 = full UI overhaul (sessions + multi-page + chat). Handoff 11 §3.2 = narrow upload-form slice.
- Stay on handoff 11 scope: upload form + nav link only. Sessions/chat/list pages → backlog.
- Stack to add: `Services/DocumentApiClient.cs`, register in `Program.cs`, `Components/Pages/DocumentUpload.razor` at `/documents/upload`, NavMenu link.

### 2026-05-26T04:25Z — D3 implementation
- `Services/DocumentApiClient.cs` (NEW, 162 lines) — `UploadAsync(stream, name, ct, size, meta) → DocumentDto`, `ListAsync(query) → IReadOnlyList<DocumentDto>`. Mirrors `AuthApiClient` shape; throws `DocumentApiException(status, code, message)`. Pinned constants `MaxFileSizeBytes=50MB` and `AllowedMimeTypes` set kept in sync with backend `DocumentService`.
- `Program.cs` (modified) — extracted `ResolveDemoUiBackendBaseUrl(IServiceProvider)` static helper (DRY between AuthApiClient + DocumentApiClient), registered `AddHttpClient<DocumentApiClient>` with 2-min timeout (large PDFs).
- `Components/Pages/DocumentUpload.razor` (NEW, 211 lines) — `@page "/documents/upload"` `@rendermode InteractiveServer`. MudForm validates `SubjectCode` (`^[A-Z]{3}[0-9]{3}$`) + `Semester` (`^(SP|SU|FA|WI)[0-9]{2}$`). MudFileUpload v9 `Hidden=true` + companion `MudButton` calls `OpenFilePickerAsync()`. Client-side guards: 50MB cap + MIME whitelist (with extension fallback for browsers that drop Office MIME). Submit calls `IBrowserFile.OpenReadStream(50MB)` + `DocumentApiClient.UploadAsync`. Toast on success, `MudAlert` on error. Maps `DocumentApiException.StatusCode` 401/413/415 to friendly copy. Auto-resets form + uploader for next upload. Auth gate on `OnInitialized` → redirect `/login`.
- `Components/Layout/NavMenu.razor` — added "Upload document" link (auth-only, after Profile).

### 2026-05-26T04:26Z — D3 build/test gate PASS
- Stopped stale app (PID 9928, was holding `apphost.exe`).
- First build FAIL on `MudFileUpload`: used MudBlazor v8-style `<ActivatorContent>` (warning RZ10012 + MUD0002). MudBlazor 9.4 dropped that template; valid slots: `CustomContent`, `SelectedTemplate`. Switched to `Hidden="true"` + sibling `MudButton OnClick=OpenFilePickerAsync`. Rebuilt.
- `dotnet build`: **0 warning, 0 error** (3.51s).
- `dotnet test`: **Passed: 38, Failed: 0, Skipped: 0** (710ms). No regression.
- D2 + D3 backend coverage gap unchanged (handoff 11 §3.4) — still scheduled for D5/SCRUM-28.

### 2026-05-26T04:27Z — D3 commit
- Staged code-only (4 files): `DocumentApiClient.cs` (NEW), `DocumentUpload.razor` (NEW), `NavMenu.razor` (M), `Program.cs` (M). Session/handoff/rule files held back for separate session-close commit per rule §5.6.
- `git commit -F <msg>` → `8454b0d feat(documents): D3 Blazor upload form (SCRUM-12/26) — code-complete`.
- 4 files / +404 / -2.
- Manual browser-based UI smoke is deferred to Kiệt (file-picker / drag-drop UX needs human eyes; backend already verified by D2 E2E).

### 2026-05-26T07:12Z — POST-GAP RECONCILE (resume after ~2h45m idle)
- Re-loaded `rule.md` + this file. Plan steps 1-4 already DONE; only step 5 (close session) remains.
- Quick state check (no full verify yet, daemon is down):
  - `git rev-parse --short HEAD` → `8454b0d` (matches `04:27Z` entry).
  - `git status --porcelain` → ` D previous_session/11_Session_2026-05-25_Sprint1_D1_D2_Handoff.md` + 3 untracked (`11a_*_superseded.md`, `_CURRENT_SESSION.md`, `rule.md`). The ` D` is from the `Rename-Item` at `03:41Z` (filesystem rename, git sees it as delete-of-old + new-untracked-file); `git mv` would be cleaner — to fix at close.
  - `docker ps` → daemon offline (`npipe ... cannot find the file specified`). Long-pause shutdown, not a crash.
  - `netstat :5240` → no listener. Backend stopped (consistent — D3 commit was the last side-effect on the app process).
- No drift vs Section 9 / last in-progress entry. Stale items spotted:
  - Section 7 Q1 already resolved by the `11a_*_superseded.md` rename at `03:41Z` — clearing.
  - Section 7 R1, R2 cleared by D2 smoke `04:19Z` (both known-unknowns landed green) — clearing.
  - Section 8 next-step text was written pre-D2-smoke and is stale — rewriting to point at the close-out path.
  - Section 9 quick facts also pre-smoke — refreshing.
- Awaiting Kiệt's decision: close session now (rule §5 full close-out) or hold.

### 2026-05-26T07:19Z — CLOSING (Kiệt confirmed: "Close session now")
- Refreshed `02_Resume_Pack.md`:
  - §1 header date → 2026-05-26, listed companion `12_*_Handoff.md` + `rule.md`.
  - §3.1 tree → added `DocumentsController`, `Folder`/`Document`/`DocumentChunk` entities + configurations, `AddDocumentSchema` migration, `DocumentDtos`, `DocumentApiException`, `SupabaseStorageClient`, `DocumentService`, `DocumentApiClient`, `Components/Pages/DocumentUpload.razor`, NavMenu link.
  - §3.2 build: 0/0 + 38/38 (post-D3 timestamp).
  - §3.3 schema: 6 public tables, 2 migrations, bucket `documents` (private/50MB/5 MIME), index list incl. ivfflat + composite subject_semester.
  - §4 phases: rows 8-10 added (D1 ✅, D2 ✅ smoke green, D3 ✅ code-complete) + D4-D6 + Sprint 2 pending rows.
  - §4.2 D2 smoke result table (8 steps GREEN, both known-unknowns cleared, commit `0245045`).
  - §4.3 D3 fact table (commit `8454b0d`, manual UI smoke deferred).
  - §9 schema: SQL for folders/documents/document_chunks + bucket DDL note.
  - §12 backlog: split into Sprint 1 (D1-D6) + Sprint 2 (RAG) with current status.
  - §13 files: added 07, 08, 09, 10, 10b, 11, 11a, 12, rule.md.
  - §14 quick facts: 11/11 expected containers, 2 migrations, bucket, current git head + 4-commit Sprint-1 trail.
- Set Status: CLOSING.
- Next: rename `_CURRENT_SESSION.md` → `12_Session_2026-05-26_Sprint1_D2smoke_D3_Handoff.md`; stage close-out files; commit `docs(session): close 12 — D2 smoke green + D3 upload form code-complete`.

| Path | Change |
|---|---|
| `previous_session/_CURRENT_SESSION.md` | created |

## 5. Commands run (chỉ những lệnh có side-effect)

(none yet)

## 6. Decisions locked

(none yet)

## 7. Open questions / risks

- ~~Q1: Có nên xoá file 11 cũ (`..._D1_D2_...`) không, hay archive để giữ history? — chờ Kiệt.~~ **Resolved 03:41Z** — renamed to `11a_*_superseded.md` per Kiệt; history preserved per rule §7.
- ~~R1: Smoke có thể fail do `[FromForm]` + `IFormFile` cùng multipart binding (handoff 11 §3.1 known unknown #1).~~ **Cleared 04:19Z** — smoke step 3 PASS, binding works.
- ~~R2: Signed URL từ Kong có thể không reachable từ trình duyệt nếu Kong route storage chưa expose đúng (handoff 11 §3.1 known unknown #4).~~ **Cleared 04:19Z** — smoke step 6 PASS, signed URL fetched anonymously through Kong, byte-equal round-trip.
- Q2 (new): Manual browser smoke của D3 upload form — Kiệt tự test khi tiện; nếu fail, mở entry `POST-D3-UI-SMOKE` ở session sau. Backend đã được D2 E2E phủ.
- R3 (new): `git status` đang thấy file 11 cũ là ` D` (delete) + file 11a là untracked, vì đã `Rename-Item` thay vì `git mv`. Phải `git add` cả hai khi commit close để git nhận diện rename (giữ blame). Không phải bug, chỉ là cosmetic ở `git log --follow`.

## 8. Next step (nếu pause/crash now)

**Plan step 5 (close session) chưa chạy — đang chờ lệnh Kiệt.** Khi Kiệt OK:

1. Refresh `02_Resume_Pack.md` §3 (schema: thêm `documents` + `document_chunks` + `folders`) và §12 backlog (D2 ✅, D3 ✅ code-complete, D4-D5 còn lại).
2. Stage close-out:
   ```
   git add previous_session/11_Session_2026-05-25_Sprint1_D1_D2_Handoff.md `
           previous_session/11a_Session_2026-05-25_Sprint1_D1_D2_Handoff_superseded.md `
           previous_session/rule.md `
           02_Resume_Pack.md
   git mv previous_session/_CURRENT_SESSION.md `
          previous_session/12_Session_2026-05-26_Sprint1_D2smoke_D3_Handoff.md
   ```
   (Set `Status: CLOSING` ở header trước khi rename.)
3. `git commit -m "docs(session): close 12 — D2 smoke green + D3 upload form code-complete"`.
4. **Không** auto-push (rule §5.6).

Nếu chưa close: file này tự nó là điểm tiếp — entry `04:27Z` là milestone cuối, mọi việc sau đó là wait-state.

## 9. Quick Facts (snapshot)

```
Containers:    0/11 running (Docker daemon down — long-pause shutdown,
               not a failure; bring back with Docker Desktop start +
               `docker compose --profile phase2 up -d` in infra/supabase/)
DB:            offline (container down)
Backend:       STOPPED @ localhost:5240 (last live during D2 smoke + D3 build)
Migrations:    2 applied (InitialSupabaseAuth, AddDocumentSchema) — persisted
               in pg volume, will reappear when DB container restarts
Tests:         38/38 last run (04:26Z, post-D3)
Build:         0 warning, 0 error (04:26Z)
Git:           main @ 8454b0d, 1 deleted + 3 untracked in previous_session/
               (all expected, will be staged at session close)
D2 smoke:      GREEN — upload/list/get/signed-download/delete all verified
               live (04:19Z); commit 0245045
D3 upload UI:  code-complete (04:27Z); commit 8454b0d; manual browser smoke
               deferred to Kiệt
```
