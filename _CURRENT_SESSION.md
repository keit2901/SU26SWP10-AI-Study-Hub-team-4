## [2026-07-10] Dashboard — Fix first-load Npgsql + clean dead code

### Root Cause Fixed
- **AdminLayout** `LoadNotificationsAsync()` từng gọi `ModerationService.GetQueueAsync()` (direct DB) cùng lúc Dashboard `LoadDashboardAsync()` (HTTP → DB), chia sẻ NpgsqlDataSource Singleton → NpgsqlOperationInProgressException
- **Fix:** AdminLayout chuyển sang dùng `DashboardApi.GetAdminStatsAsync()` (HTTP) thay vì direct DB call. Bỏ `@inject IDocumentModerationService`. Exception không còn bị nuốt lặng (`catch { }`) → giờ log warning qua `ILogger<AdminLayout>`.
- **Dashboard:** Xóa `@inject IDocumentModerationService`, xóa `ModerationService.GetQueueAsync()` + `ApplyModerationQueue()` trong `LoadDashboardAsync()`.

### Frontend Fixes (Dashboard.razor)
- Donut center: hardcode "155" → `@_metrics.TotalDocuments`
- `_statusLabels` order: `{Pending,Processing,Indexed,Failed}` → `{Indexed,Processing,Pending,Failed}` (khớp ApplyStats)
- Xóa dead code: `StatusSeries` property, `GetStatusClass`, `GetSeverityClass`, `GetDocumentStatusColor`, `GetSeverityColor`, `GetTokenProgressColor`
- "Documents needing attention": thay MudTable cũ (dùng `_attentionDocuments`) bằng summary card (Pending/Failed/Processing counts từ `_metrics`)
- Recommended Actions: `_attentionDocuments.Count` → `_metrics.PendingDocuments`
- DashboardMetrics: thêm `PendingDocuments` field, ApplyStats set `ProcessingDocuments` + `PendingDocuments` từ real data
- Ẩn: `NewUsersThisWeek` "+0 this week", `High-usage users` count cứng
- Xóa `AttentionDocument` record

### Files Changed
- `Components/Admin/Shared/AdminLayout.razor`
- `Components/Admin/Dashboard.razor`

### Build
- `dotnet build` — 0 errors, 0 warnings mới

### Verify
- App chạy localhost:5240, Dashboard first-load không còn "Hệ thống đang bận", không Npgsql exception
- Notification badges load từ stats API (HTTP)

### Remaining
- Push PR (commit + push feature/admin-upgrade)
- ADM-06: Escalations (đã phân tích: thiếu GetAllAsync, GetMyAsync, controller UserId lookup)
