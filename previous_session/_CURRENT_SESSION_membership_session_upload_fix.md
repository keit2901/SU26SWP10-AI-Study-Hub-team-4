# _CURRENT_SESSION - membership_session_upload_fix

**Started:** 2026-07-21T12:09:25.5122140Z
**Agent:** Codex (GPT-5)
**Goal:** Fix student plan display/session persistence/payment expiry/upload quota behavior, and add a seeded Pro student account path for local development.
**Status:** IN_PROGRESS

---

## 0. Context loaded
- [x] `previous_session/rule.md` (read 2026-07-21T12:09:25.5122140Z)
- [x] `Project-Docs/skill.md` (read 2026-07-21T12:09:25.5122140Z)
- [x] `previous_session/handoff_2026-07-10.md` (read 2026-07-21T12:09:25.5122140Z)
- [x] `previous_session/_CURRENT_SESSION_subscription_payment_bug.md` (read 2026-07-21T12:09:25.5122140Z)
- [x] `previous_session/_CURRENT_SESSION_folder_quota_upload_fix.md` (read 2026-07-21T12:09:25.5122140Z)
- [x] `previous_session/_CURRENT_SESSION_student_profile_menu.md` (read 2026-07-21T12:09:25.5122140Z)

## 1. Verified state at start
- `git status --short --branch --untracked-files=all` -> branch `feature/Dashbroad(moderator-sort-filter)-analytics(box-deletion)...origin/feature/Dashbroad(moderator-sort-filter)-analytics(box-deletion) [ahead 4]`
- Relevant files confirmed:
  - `AI_Study_Hub_v2/Components/Shared/UserAccountMenu.razor`
  - `AI_Study_Hub_v2/Components/Shared/StudentProfilePanel.razor`
  - `AI_Study_Hub_v2/Components/Pages/Pricing.razor`
  - `AI_Study_Hub_v2/Components/Pages/PricingCheckoutDialog.razor`
  - `AI_Study_Hub_v2/Components/Pages/PaymentResult.razor`
  - `AI_Study_Hub_v2/Components/Pages/DocumentUpload.razor`
  - `AI_Study_Hub_v2/Services/AuthPersistenceService.cs`
  - `AI_Study_Hub_v2/Services/AuthSessionState.cs`
  - `AI_Study_Hub_v2/Services/StorageQuotaService.cs`
  - `AI_Study_Hub_v2/Services/Payment/PaymentService.cs`
  - `AI_Study_Hub_v2/Program.cs`

## 2. Plan
1. Replace hardcoded student plan labels with real current-plan snapshot data everywhere relevant.
2. Extend auth persistence to survive longer and auto-refresh expired access tokens from the stored refresh token.
3. Reduce payment expiry to 2 minutes and surface expired-payment UX that returns the student to pricing.
4. Make upload quota/file-limit UI and enforcement respect the active plan instead of fixed Free defaults.
5. Add a local-development seeded Pro student account configuration path.

## 3. Progress log (append-only, newest last)

### 2026-07-21T12:09:25.5122140Z - Discovery completed
- Confirmed `UserAccountMenu.razor` still hardcodes `Free` in the top-right student trigger.
- Confirmed `StudentProfilePanel.razor` and payment history already depend on `Session.LoadCurrentPlanAsync`, but the shared session state only stores a plan key and the menu never binds a real plan snapshot.
- Confirmed auth persistence still uses `ProtectedSessionStorage` and only restores stored tokens; it does not auto-refresh when `ExpiresAt` is past.
- Confirmed payment expiry is controlled by `PayOsSettings.ExpireMinutes` and `PaymentService`, currently defaulting to 15 minutes.
- Confirmed `DocumentUpload.razor`, `DocumentApiClient`, `DocumentsController`, and `DocumentService` still enforce/display a hardcoded 50 MB upload limit even though seeded Pro plan metadata allows 100 MB.

### 2026-07-21T12:45:00.0000000Z - Membership/session/payment/upload patch implemented
- Updated auth/session persistence:
  - `AI_Study_Hub_v2/Services/AuthApiClient.cs` -> added `RefreshAsync`
  - `AI_Study_Hub_v2/Services/AuthPersistenceService.cs` -> switched to `ProtectedLocalStorage` and auto-refreshes expired access tokens with the stored refresh token
  - `AI_Study_Hub_v2/Services/AuthSessionState.cs` -> now tracks the full current plan snapshot/display name instead of a key-only demo value
- Updated student plan display:
  - `AI_Study_Hub_v2/Components/Shared/UserAccountMenu.razor` -> removed hardcoded `Free`, loads real plan snapshot for the top-right student chip
  - `AI_Study_Hub_v2/Components/Shared/StudentProfilePanel.razor` -> uses the shared display name from session state
- Updated plan snapshot payload:
  - `AI_Study_Hub_v2/Dtos/StorageQuotaSnapshotDto.cs` -> now carries file/document/folder limit fields
  - `AI_Study_Hub_v2/Services/StorageQuotaService.cs` -> populates the extra plan-limit fields from the effective plan
- Updated upload flow to respect the purchased plan:
  - `AI_Study_Hub_v2/Services/DocumentApiClient.cs` + `AI_Study_Hub_v2/Controllers/DocumentsController.cs` + `AI_Study_Hub_v2/Services/DocumentService.cs`
    - absolute backend cap raised to 250 MB
    - per-plan file-size enforcement now comes from the active plan snapshot
  - `AI_Study_Hub_v2/Components/Pages/DocumentUpload.razor` + `.razor.css`
    - upload sidebar now shows current plan, used storage, remaining storage, and per-file limit
    - client-side validation/messages now use the active plan limits instead of fixed 50 MB text
- Updated payment expiry behavior:
  - `AI_Study_Hub_v2/Options/PayOsSettings.cs` -> default expiry reduced to 2 minutes
  - `AI_Study_Hub_v2/Services/Payment/PaymentService.cs` -> pending transactions are marked `expired` immediately when return verification sees they have timed out
  - `AI_Study_Hub_v2/Components/Pages/PaymentResult.razor` -> added explicit expired-payment state and auto-return to pricing
  - `AI_Study_Hub_v2/Components/Pages/PricingCheckoutDialog.razor` -> surfaces the 2-minute payment window to the student before redirect
- Added local Pro student seed path:
  - `AI_Study_Hub_v2/Options/SeedOptions.cs`
  - `AI_Study_Hub_v2/Program.cs`
  - `AI_Study_Hub_v2/appsettings.json`
  - `AI_Study_Hub_v2/appsettings.Development.json`
  - startup can now create/maintain a configured `DefaultProStudent` account and keep it on the Pro plan without committing any plaintext password

### 2026-07-21T13:10:00.0000000Z - Verification completed
- `dotnet build "AI_Study_Hub_v2\\AI_Study_Hub_v2.csproj" --nologo --no-restore -p:UseAppHost=false` -> PASS (0 errors, 0 warnings)
- `dotnet build "AI_Study_Hub_v2\\AI_Study_Hub_v2.Tests\\AI_Study_Hub_v2.Tests.csproj" --nologo --no-restore -o .codex-build\\membership-fix-tests` -> PASS (0 errors, 0 warnings)
- `dotnet vstest ".codex-build\\membership-fix-tests\\AI_Study_Hub_v2.Tests.dll" --TestCaseFilter:"FullyQualifiedName~SupabaseAuthServiceTests|FullyQualifiedName~StorageQuotaServiceTests|FullyQualifiedName~PlansControllerTests"` -> PASS
  - Result: Passed 39, Skipped 2, Failed 0

## 4. Files changed this session
| Path | Change |
|---|---|
| `AI_Study_Hub_v2/Services/AuthApiClient.cs` | added refresh-token API call |
| `AI_Study_Hub_v2/Services/AuthPersistenceService.cs` | local persisted session + auto refresh |
| `AI_Study_Hub_v2/Services/AuthSessionState.cs` | current plan snapshot/display state |
| `AI_Study_Hub_v2/Components/Shared/UserAccountMenu.razor` | top-right user chip uses real plan |
| `AI_Study_Hub_v2/Components/Shared/StudentProfilePanel.razor` | profile plan label uses shared display name |
| `AI_Study_Hub_v2/Dtos/StorageQuotaSnapshotDto.cs` | added plan limit fields |
| `AI_Study_Hub_v2/Services/StorageQuotaService.cs` | returns plan limit fields in snapshot |
| `AI_Study_Hub_v2/Services/DocumentApiClient.cs` | absolute upload cap aligned to dynamic plan enforcement |
| `AI_Study_Hub_v2/Controllers/DocumentsController.cs` | request cap aligned to dynamic plan enforcement |
| `AI_Study_Hub_v2/Services/DocumentService.cs` | per-plan file-size enforcement |
| `AI_Study_Hub_v2/Components/Pages/DocumentUpload.razor` | dynamic upload quota UI + validation |
| `AI_Study_Hub_v2/Components/Pages/DocumentUpload.razor.css` | styling for quota summary panel |
| `AI_Study_Hub_v2/Options/PayOsSettings.cs` | default payment expiry set to 2 minutes |
| `AI_Study_Hub_v2/Services/Payment/PaymentService.cs` | return-time expiry handling |
| `AI_Study_Hub_v2/Components/Pages/PaymentResult.razor` | expired-payment UX + redirect |
| `AI_Study_Hub_v2/Components/Pages/PricingCheckoutDialog.razor` | payment-window notice |
| `AI_Study_Hub_v2/Options/SeedOptions.cs` | added `DefaultProStudent` config model |
| `AI_Study_Hub_v2/Program.cs` | added Pro student seed routine |
| `AI_Study_Hub_v2/appsettings.json` | base config for `DefaultProStudent` + PayOS expiry |
| `AI_Study_Hub_v2/appsettings.Development.json` | local dev Pro student identity metadata + PayOS expiry |
| `previous_session/_CURRENT_SESSION_membership_session_upload_fix.md` | created live session log |

## 5. Commands run (only side-effect / stateful)
- `git status --short --branch --untracked-files=all`
- `dotnet build "AI_Study_Hub_v2\\AI_Study_Hub_v2.csproj" --nologo --no-restore -p:UseAppHost=false` -> PASS
- `dotnet build "AI_Study_Hub_v2\\AI_Study_Hub_v2.Tests\\AI_Study_Hub_v2.Tests.csproj" --nologo --no-restore -o .codex-build\\membership-fix-tests` -> PASS
- `dotnet vstest ".codex-build\\membership-fix-tests\\AI_Study_Hub_v2.Tests.dll" --TestCaseFilter:"FullyQualifiedName~SupabaseAuthServiceTests|FullyQualifiedName~StorageQuotaServiceTests|FullyQualifiedName~PlansControllerTests"` -> Passed 39, Skipped 2, Failed 0

## 6. Decisions locked
- Use the current PayOS-based payment flow (`PaymentService`) as the source of truth; do not revive archived VNPay logic.
- Prefer extending current plan snapshot/session services rather than duplicating plan queries per component.

## 7. Open questions / risks
- The seeded Pro student path is implemented, but the actual account will only be created on startup when `Seed:DefaultProStudent:Password` exists in local user-secrets or another secure config source. No plaintext password was committed.

## 8. Next step (if pause/crash now)
Patch `AuthPersistenceService`, plan snapshot DTO/state, upload limit enforcement, and PayOS expiry flow together, then run focused tests/build verification.

## 9. Quick Facts (snapshot)
- Current date: 2026-07-21
- Payment provider in use: PayOS (`PaymentService`)
- Existing seeded plan limits: `free` 50 MB/file, `pro` 100 MB/file, `unlimited` null file limit in DB
