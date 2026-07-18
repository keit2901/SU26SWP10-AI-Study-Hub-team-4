# _CURRENT_SESSION - folder_quota_upload_fix

**Started:** 2026-07-15T09:15+07:00
**Agent:** Codex (GPT-5)
**Goal:** Fix document upload being misclassified as creating a new folder, which triggers `folder_count_exceeded` when uploading into an existing folder.
**Status:** IN_PROGRESS

---

## 0. Context loaded
- Upload runtime log from local app showing `POST /api/documents/upload` ends with `PlanException: Folder count limit reached.`
- `DocumentService.cs`
- `PlanCapacityGuard.cs`
- `DocumentsController.cs`
- Relevant controller/service tests

## 1. Root cause
- `DocumentService.UploadAsync` finalized metadata with `new PlanCapacityRequest(1, 0, request.FolderId, request.FolderId.HasValue ? 1 : 0)`.
- `PlanCapacityGuard.ValidateCapacityAsync` always counted folders and evaluated `MaxFolderCount`, even when `AdditionalFolderCount == 0`.
- `DocumentsController.Upload` did not catch `PlanException`, so the user saw a generic `500` instead of a quota error payload.

## 2. Changes made
- `AI_Study_Hub_v2/Services/DocumentService.cs`
  - Upload finalization now passes `new PlanCapacityRequest(1, 0, request.FolderId, request.FolderId.HasValue ? 1 : 0, 0)`.
- `AI_Study_Hub_v2/Services/PlanCapacityGuard.cs`
  - Folder quota check now runs only when `request.AdditionalFolderCount > 0`.
- `AI_Study_Hub_v2/Controllers/DocumentsController.cs`
  - Added `catch (PlanException ex)` and mapped it to `ApiErrorResponse`.
- `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/PlanCapacityGuardTests.cs`
  - Added `LockAndValidateAsync_ExistingFolderUpload_DoesNotApplyFolderCountLimit`.
- `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/PlanCapacityPostgresTests.cs`
  - Updated finalization request to include the explicit `NewFolderDocumentCount: 0`.
- `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Controllers/DocumentsControllerTests.cs`
  - Added controller mapping test for `PlanException`.

## 3. Verification
- Built isolated test output:
  - `dotnet build AI_Study_Hub_v2.Tests.csproj --nologo -o .codex-build/folder-quota-fix-tests` -> PASS
- Ran focused regression tests from isolated DLL:
  - `dotnet vstest .codex-build/folder-quota-fix-tests/AI_Study_Hub_v2.Tests.dll --TestCaseFilter:"FullyQualifiedName~DocumentsControllerTests|FullyQualifiedName~PlanCapacityGuardTests"` -> PASS
  - Result: `Passed: 41, Skipped: 2, Failed: 0`

## 4. Notes
- The ordinary `dotnet test` path was blocked by the running local app locking `bin\Debug\net8.0\AI_Study_Hub_v2.dll`.
- Using isolated build output avoided stopping the user's running app.
