## [2026-07-19] — Users & Permissions + Audit Logs + Share Review Plan

### Branch: `feature/admin-upgrade`
### PR: #71 — merged with main, pushed, no conflicts

---

### Users & Permissions — Done

| Feature | Status |
|---------|--------|
| 4-status widget (Active/Inactive/Banned/Previously Banned) | ✅ |
| Status filter dropdown with Banned + Previously Banned | ✅ |
| Click user name → popup detail (UserDetailDialog via DialogService) | ✅ |
| Profile dropdown redesign (MudMenu → MudPopover) | ✅ |
| Fix admin logout (GoTrue SignOut + forceLoad) | ✅ |
| Quota dialog (QuotaEditDialog via DialogService) | ✅ |
| UserDetail placeholder "Supabase..." → real info | ✅ |
| "LAST ACTIVE" → "CREATED" with real data | ✅ |
| Remove 3-dot menu (⋮), FormatLastActive dead code | ✅ |
| Widgets: Role Distribution, Account Status, Escalations → actionable | ✅ |
| Guardrail alert compact + View audit logs link | ✅ |
| High-usage users tab: fix contradicting warning + empty state | ✅ |
| Filter reset buttons across all admin tabs | ✅ |
| Remove Folder Moderation tab from AdminLayout | ✅ |
| Backend: IsPreviouslyBanned field (removed IsActive requirement) | ✅ |
| Backend: MatchesStatus add "Banned" case | ✅ |

### Audit Logs — Done

| Feature | Status |
|---------|--------|
| Fix expand-all bug (RequestId → Id) | ✅ |
| Date range with presets (All/7d/30d) | ✅ |
| Entity Type filter dropdown | ✅ |
| Actor summary cards (click to filter) | ✅ |
| 7-tab category dashboard | ✅ |
| Total events badge, filter reset button | ✅ |
| Export disabled, fake pagination removed | ✅ |
| AuditLogServiceTests (6 tests) | ✅ |
| AuditLogsControllerTests (3 tests) | ✅ |

### Escalation — Done

| Feature | Status |
|---------|--------|
| EscalationService: GetAllAsync, GetMyAsync | ✅ |
| EscalationController: GetLocalUserIdAsync (FK fix), /all, /my | ✅ |
| EscalationApiClient: GetAllAsync, GetMyAsync | ✅ |
| AdminEscalations.razor: full rewrite (DocumentMod UI style) | ✅ |
| EscalationServiceTests (11 tests) | ✅ |
| EscalationControllerTests (4 tests) | ✅ |
| Escalation audit logging (CREATE + RESOLVE) | ✅ |

### Document Moderation — Removed

| File | Status |
|------|--------|
| DocumentModeration.razor | ❌ Deleted |
| DocumentModerationController.cs | ❌ Deleted |
| DocumentModerationService.cs | ❌ Deleted |
| IDocumentModerationService.cs | ❌ Deleted |
| Program.cs DI registration | ❌ Removed |
| AdminLayout sidebar, footer, notification, breadcrumb | ❌ Cleaned |

### Tests

| Result | Count |
|--------|-------|
| Passed | 439 |
| Failed | 0 |
| Skipped | 35 (Postgres/Ollama) |

### Dashboard — Done (PR #55)

- Npgsql first-load fix (AdminLayout HTTP instead of direct DB)
- Attention card UX: context-aware CTA, all-clear state, timestamp
- Activity Trends: removed hardcoded fallback → empty/error states
- Rename "Active Sessions" → "Active Jobs"
- Donut center, labels order, dead code cleanup
- Remove hardcoded KPI, hidden "+0 this week"
- Filter reset button fix

### Lost in Merge (need re-apply)

Audit logging for 8 services was overwritten during merge from main:
- DocumentService (DOCUMENT_UPLOADED, DOCUMENT_DELETED)
- FolderService (FOLDER_SHARE_REQUESTED/APPROVED/REJECTED/COPIED)
- ChatPersistenceService (AI_CHAT_SESSION_CREATED, AI_CHAT_EXCHANGE)
- CommunityService (FOLDER_REPORTED, REPORT_RESOLVED)
- AiAnswerReportService (AI_ANSWER_REPORTED)
- DashboardService (DOCUMENT_APPROVED, DOCUMENT_REJECTED)
- QuizService (QUIZ_GENERATED, QUIZ_SUBMITTED)
- SupabaseAuthService (USER_REGISTERED, USER_LOGOUT)

### Planned (Share Review Feature)

Plan saved to: `C:\Users\ADMIN\Downloads\ShareReview-Plan.md`
- 3-step wizard: Landing → Per-File Review → Celebration
- 3-level delete confirmation: Inline → Batch → 30s Undo Timer
- 12 premium features: AI confidence, context snippet, severity, smart suggestions
- ~1,127 lines, 8 files

### Waiting
- PR #71 review/merge
- Re-apply lost audit logging (8 services)
- Implement Share Review feature
