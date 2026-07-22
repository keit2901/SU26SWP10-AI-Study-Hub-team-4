# List làm việc — Admin Sidebar (8 tabs)
> Branch: feature/admin-upgrade

| ID | Tab | Status |
|----|-----|--------|
| ADM-01 | Dashboard | ✅ Done |
| ADM-02 | Users & Permissions | ✅ Done |
| ADM-03 | Community Reports | ✅ Done |
| ADM-04 | Document Library | ✅ Done |
| ADM-05 | Escalations | ⚪ Chưa check |
| ADM-06 | AI System Settings | ✅ Done |
| ADM-07 | Benchmark History | ✅ Done |
| ADM-08 | Audit Logs | ✅ Done |

---

# Session — 2026-07-07

## Đã làm
- Dashboard: KPIs + charts + critical events + token usage + action list → tất cả wire real API
- Users: All Users tab + High-usage Users tab + query param ?quota=warning
- Dashboard CTA → /admin/users?quota=warning
- BuildSeedUsers/BuildAttentionDocuments/BuildRecentActivities → đã xóa hết, dùng API thật
- AdminLayout notification badges → real data
- Fix NpgsqlOperationInProgressException: AppDbContext Transient + sequential await + remove Include(Chunks)

## Còn tồn tại
- Dashboard first-load: admin-dashboard-stats query xong nhưng moderation queue query (GetQueueAsync) bị NpgsqlOperationInProgressException do Dashboard + Users load cùng circuit
- AppDbContext đã chuyển Transient nhưng lỗi vẫn còn → root cause nằm ở service layer (DocumentModerationService.GetQueueAsync), không phải UI
- Npgsql connection pool conflict khi nhiều request cùng gọi GetQueueAsync

## Next
- Sửa DocumentModerationService.GetQueueAsync: gom 2 query thành 1 hoặc dùng scope riêng
- Hoặc bỏ GetQueueAsync khỏi Dashboard, chỉ dùng cho Moderation page
- ADM-02 Users: test kỹ lại sau khi sửa Npgsql

## Branch
feature/admin-upgrade — đã push, PR chưa tạo
