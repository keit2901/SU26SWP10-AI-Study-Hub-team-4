# _CURRENT_SESSION - subscription_payment_bug

**Started:** 2026-07-17T11:31:06+07:00
**Agent:** Codex (GPT-5)
**Goal:** Diagnose and fix the subscription/VNPay redirect flow so failed external payment attempts do not log the user out, do not show incorrect downgraded pricing, and do not block re-attempts with 409.
**Status:** IN_PROGRESS

---

## 0. Context loaded
- [x] `previous_session/rule.md` (read 2026-07-17T11:31:06+07:00)
- [x] `previous_session/handoff_backend_2026-06-17.md` (read 2026-07-17T11:31:06+07:00)
- [x] `previous_session/handoff_2026-07-10.md` (read 2026-07-17T11:31:06+07:00)
- [ ] `previous_session/skill.md` (missing in workspace; continued with best-effort)

## 1. Verified state at start
- `git status --short --branch --untracked-files=all` -> branch `feature/Dashbroad(moderator-sort-filter)-analytics(box-deletion)...origin/feature/Dashbroad(moderator-sort-filter)-analytics(box-deletion)`
- Located payment flow in:
  - `AI_Study_Hub_v2/Components/Pages/Pricing.razor`
  - `AI_Study_Hub_v2/Components/Pages/PricingCheckoutDialog.razor`
  - `AI_Study_Hub_v2/Components/Pages/PaymentResult.razor`
  - `AI_Study_Hub_v2/Controllers/PlansController.cs`
  - `AI_Study_Hub_v2/Controllers/VnPayController.cs`
  - `AI_Study_Hub_v2/Services/Payment/VnPayService.cs`

## 2. Plan
1. Restore auth/session correctly after external VNPay redirects on pricing/payment result pages.
2. Stop pricing page from falling back to stale hardcoded prices after auth loss.
3. Make repeated payment attempts reuse or recover the pending VNPay transaction instead of returning 409.
4. Add regression coverage and verify with build/tests.

## 3. Progress log (append-only, newest last)

### 2026-07-17T11:31:06+07:00 - Root-cause analysis completed
- Confirmed `AuthSessionState` still documents `in-memory only - refresh page = logged out`, which explains logout after navigating away to VNPay and returning via full page load.
- Confirmed `Pricing.razor` falls back to `GetDefaultPlans()` whenever plan API loading fails; those hardcoded prices (`49,000` / `490,000`) differ from seeded DB prices (`50,000` / `500,000`), explaining the observed price drop after the first failed payment attempt.
- Confirmed `VnPayService.CreatePaymentAsync` blocks a second attempt while an earlier `pending` payment still exists and has not expired, which explains the later `409` after the failed redirect path when no callback marks the transaction `failed`/`expired`.

### 2026-07-17T11:40:35+07:00 - Fixes implemented and verified
- Edited `AI_Study_Hub_v2/Components/Pages/Pricing.razor`
  - restore persisted auth session on page init
  - load plan catalog without requiring auth
  - refresh current plan after restore
  - align fallback hardcoded prices with seeded DB values
- Edited `AI_Study_Hub_v2/Components/Pages/PaymentResult.razor`
  - restore persisted auth session before verification
  - allow return-url verification even when access token is absent
- Edited `AI_Study_Hub_v2/Services/PlanApiClient.cs`
  - made `GetPlansAsync` and `VerifyReturnUrlAsync` accept optional bearer token
- Edited `AI_Study_Hub_v2/Controllers/PlansController.cs`
  - made `GET /api/plans` anonymous so pricing does not fall back to stale client defaults after redirect/session loss
- Edited `AI_Study_Hub_v2/Controllers/VnPayController.cs`
  - made `GET /api/vnpay/return` anonymous
- Edited `AI_Study_Hub_v2/Services/Payment/VnPayService.cs`
  - reuse same pending VNPay transaction when user retries the same plan/cycle
  - expire superseded pending transactions when a new attempt changes plan/cycle
- Added `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/VnPayServiceTests.cs`
  - covers pending-transaction reuse
  - covers supersede/new-transaction flow
- Verification:
  - `dotnet test "AI_Study_Hub_v2\\AI_Study_Hub_v2.Tests\\AI_Study_Hub_v2.Tests.csproj" --nologo --filter "FullyQualifiedName~PlansControllerTests|FullyQualifiedName~VnPayServiceTests"` -> Passed 14, Skipped 1
  - `dotnet build "AI_Study_Hub_v2\\AI_Study_Hub_v2.sln" --nologo` -> blocked by file lock on `AI_Study_Hub_v2.dll` from running `.NET Host` PID `10808`, not by source compile errors

## 4. Files changed this session
| Path | Change |
|---|---|
| `AI_Study_Hub_v2/Components/Pages/Pricing.razor` | restore session + public plan loading + fallback price alignment |
| `AI_Study_Hub_v2/Components/Pages/PaymentResult.razor` | restore session + anonymous payment verification |
| `AI_Study_Hub_v2/Services/PlanApiClient.cs` | optional auth for plan list and VNPay return verification |
| `AI_Study_Hub_v2/Controllers/PlansController.cs` | allow anonymous plan catalog |
| `AI_Study_Hub_v2/Controllers/VnPayController.cs` | allow anonymous return callback |
| `AI_Study_Hub_v2/Services/Payment/VnPayService.cs` | reuse/supersede pending payments instead of 409 block |
| `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/VnPayServiceTests.cs` | added regression tests for pending payment behavior |
| `previous_session/_CURRENT_SESSION_subscription_payment_bug.md` | created live session log |

## 5. Commands run (only side-effect / stateful)
- `git status --short --branch --untracked-files=all`
- `dotnet build "AI_Study_Hub_v2\\AI_Study_Hub_v2.sln" --nologo` (sandbox blocked by NuGet network; later rerun unsandboxed and then blocked by running host file lock)
- `dotnet test "AI_Study_Hub_v2\\AI_Study_Hub_v2.Tests\\AI_Study_Hub_v2.Tests.csproj" --nologo --filter "FullyQualifiedName~PlansControllerTests|FullyQualifiedName~VnPayServiceTests"` -> Passed 14, Skipped 1

## 6. Decisions locked
- Keep the fix targeted to subscription/payment flow only; do not alter unrelated auth flows.
- Chose to make plan catalog and VNPay return verification anonymous because both endpoints only need catalog/transaction state and this removes redirect-induced auth brittleness.
- Chose to reuse matching pending VNPay transactions instead of returning `409`, and to expire superseded pending transactions when the user starts a new attempt.

## 7. Open questions / risks
- Standalone `dotnet build` remains blocked while a running local `.NET Host` keeps `AI_Study_Hub_v2.dll` open; stop the dev host before demanding a clean separate build command.

## 8. Next step (if pause/crash now)
If further verification is needed, stop the local dev host holding `AI_Study_Hub_v2.dll`, then rerun `dotnet build "AI_Study_Hub_v2\\AI_Study_Hub_v2.sln" --nologo"`.

## 9. Quick Facts (snapshot)
- Backend preview convention: `http://localhost:5240/`
- Payment flow endpoints: `/pricing`, `/payment/result`, `/api/plans/purchase`, `/api/vnpay/return`
- Local lock holder observed during build: `.NET Host` PID `10808`
