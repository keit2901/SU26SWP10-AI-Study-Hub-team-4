# Current Session — 2026-06-22 — Quiz & Folder Isolation Fixes

## Completed

### 1. Folder isolation fix — race condition + server-side filter

- **Root cause**: `_ = LoadSessionsAsync()` (fire-and-forget) during page init could race with folder switch. If the init response arrived after folder switch, it overwrote the cleared state with the *previous* folder's messages.
- **Fix (version counter)**: Added `_sessionLoadVersion` (int) to `AiChat.razor`. Both `LoadSessionsAsync` and `LoadSessionMessagesAsync` capture a snapshot of the version on entry and discard the result if it doesn't match the current version. This prevents stale responses from overwriting newer state.
- **Fix (server-side filtering)**: Added optional `Guid? folderId` parameter to `ListSessionsAsync` across `IChatPersistenceService` → `ChatPersistenceService` → `AiChatController` → `AiChatApiClient`. The EF Core query now filters at the database level via `.Where(s => !folderId.HasValue || s.FolderId == folderId)`. The client-side `.Where(...)` filter in `AiChat.razor` was removed.
- **Files modified**:
  - `AiChat.razor` — version counter + `_expectedFolderId` tracking + client-side `.Where()` filter + pass `_folderId` to API
  - `IChatPersistenceService.cs` — added `folderId` parameter
  - `ChatPersistenceService.cs` — added DB-level filter (server-side)
  - `AiChatController.cs` — accept `[FromQuery] Guid? folderId`
  - `AiChatApiClient.cs` — pass query param

### 6. Folder isolation fix — round 2 (validation layers)
- **Problem persisted**: version counter + server-side filter alone didn't fully prevent cross-contamination.
- **Added `_expectedFolderId`**: tracks which folder triggered the current `LoadSessionsAsync`. `LoadSessionMessagesAsync` checks `_expectedFolderId != _folderId` after the API completes and discards stale results if they no longer match the current folder.
- **Restored client-side `.Where(s => !folderId.HasValue || s.FolderId == folderId.Value)`** as defense-in-depth.
- **Cleared `_resumeQuiz`** in `OnWorkspaceFolderChanged` alongside session/history reset.
- **Current layers of defense**:
  1. Server-side DB filter (EF WHERE clause)
  2. Version counter (`_sessionLoadVersion`) — discards stale API responses
  3. `_expectedFolderId` check — discards messages loaded for wrong folder
  4. Client-side `.Where()` filter — catches any server-side data leaks

### 2. Quiz status persistence fix — stale "InProgress" after restart

- **Root cause 1**: `UpdateQuizMetadataAsync` used `.Contains(quizId.ToString())` on a `jsonb` column. PostgreSQL `jsonb` doesn't support `LIKE`/`strpos` directly; EF Core's translation either fails or returns no results. Metadata was never updated, so quiz status stayed "InProgress".
  - **Fix**: Changed to `FromSqlRaw` with the `->>'QuizId'` JSON operator, which correctly extracts the text value from the JSONB column.
- **Root cause 2**: Even if metadata *were* updated, the lightweight `QuizDto` deserialized from message metadata would only hold the status at the time the message was saved. On restart, the entity table has the correct "Completed" status, but the metadata never gets refreshed.
  - **Fix**: Added `RefreshQuizStatusesAsync` in `LoadSessionMessagesAsync`. After converting messages to exchanges, it iterates over any quizzes, checks the local `_quizCache`, and fetches the fresh quiz entity from the API (which queries the DB). The exchange is updated in-place with the correct status.
- **Files modified**:
  - `ChatPersistenceService.cs` — raw SQL JSONB query
  - `AiChat.razor` — `RefreshQuizStatusesAsync` method

### 3. Build verification
- `dotnet build` — 0 errors, 7 warnings (all pre-existing)
