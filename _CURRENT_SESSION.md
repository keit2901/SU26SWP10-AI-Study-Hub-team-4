## [2026-07-10] Dashboard fixes — committed + pushed `feature/admin-upgrade`

### Commit: `a96b491` — `fix(dashboard): Npgsql first-load + clean dead code + refine attention card UI`

### Files changed (3)

| File | Changes |
|------|---------|
| `Dashboard.razor` | 234 lines — bỏ dead code, fix UI binding, attention card UX |
| `AdminLayout.razor` | 19 lines — replace direct DB with HTTP stats API |
| `_CURRENT_SESSION.md` | Session log |

### Fixed

**P1 — Npgsql first-load:**
- AdminLayout: `ModerationService.GetQueueAsync()` → `DashboardApi.GetAdminStatsAsync()` (HTTP)
- Dashboard: removed `@inject IDocumentModerationService`, removed `GetQueueAsync()` + `ApplyModerationQueue()`

**P2 — Dead code cleanup:**
- Removed `GetStatusClass`, `GetSeverityClass`, `GetDocumentStatusColor`, `GetSeverityColor`, `GetTokenProgressColor`
- Removed `StatusSeries` property, `AttentionDocument` record, `_attentionDocuments` field
- Removed `ApplyModerationQueue` method

**P3 — Data binding fixes:**
- Donut center: `155` → `@_metrics.TotalDocuments`
- `_statusLabels`: `{Pending,...}` → `{Indexed,Processing,Pending,Failed}` (matches ApplyStats)
- `DashboardMetrics`: added `PendingDocuments` field
- `ApplyStats`: set `ProcessingDocuments` + `PendingDocuments` from real data
- Activity Trends: removed hardcoded fallback data → empty/error states

**P4 — Attention card UX:**
- Context-aware CTA: Failed > Pending > Processing > View queue
- All-clear state: check icon + "All documents are clear."
- `_lastDashboardLoadedAt` timestamp (set after successful load, not in markup)
- Removed `+0 this week`, `High-usage users` hardcode
- Renamed "Active Sessions" → "Active Jobs"

### Build: 0 errors

### Remaining
- **ADM-06: Escalations** — backend missing GetAllAsync/GetMyAsync, controller FK bug
