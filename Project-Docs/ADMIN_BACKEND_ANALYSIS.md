# Admin Backend — Full Analysis & Progress List

> Updated: 2026-07-07 | Branch: feature/admin-upgrade

---

## 8 Admin Pages — Status (Subjects đã xóa)

| # | Page | Status | API Client | Data Source |
|---|------|--------|-----------|-------------|
| 1 | Dashboard.razor | 🟢 Real | AdminDashboardApiClient + IDocumentModerationService | Real API |
| 2 | Users/Users.razor | 🟡 Mixed | AdminApiClient | Real: list. Mock: — |
| 3 | Users/UserDetail.razor | 🔴 Mock | None | BuildUser(), BuildDocuments(), BuildActivities() |
| 4 | Users/InviteUserSheet.razor | ⚪ N/A | None | Pure UI form component |
| 5 | Documents/Documents.razor | 🟢 Real | AdminApiClient | ListDocumentsAsync |
| 6 | Documents/DocumentDetail.razor | 🟢 Real | AdminApiClient | GetDocumentDetailAsync |
| 7 | Documents/DocumentModeration.razor | 🟢 Real | IDocumentModerationService | Queue API, xóa mock fallback |
| 8 | Settings/SystemSettings.razor | 🟢 Real | SystemSettingsApiClient | GET/PUT api/admin/settings |
| 9 | ~~Subjects/Subjects.razor~~ | ❌ Deleted | — | Đã xóa khỏi Admin |
| 10 | AuditLogs/AuditLogs.razor | 🟢 Real | AdminApiClient | ListAuditLogsAsync |
| 11 | Community/Reports.razor | 🟢 Real | CommunityApiClient | GetPendingReportsAsync |

---

## Existing Backend Controllers (Admin-related)

| Controller | Route | Endpoints |
|-----------|-------|-----------|
| AdminUsersController | api/admin/users | GET list, PATCH quota/role/activate/deactivate |
| AdminDocumentsController | api/admin/documents | POST reingest-all (thiếu GET list, GET detail) |
| DocumentModerationController | api/admin/moderation | GET queue/escalated, POST approve/reject/escalate/restore, DELETE |
| DashboardController | api/dashboard | GET admin/stats, GET admin/activity-trends |
| AuditLogsController | api/admin/audit-logs | GET list (action/from/to/limit filters) |
| RolesController | api/roles | GET list |
| CommunityController | api/community | POST report, GET pending, PATCH resolve |

---

## Missing Backend

| Priority | Controller/Endpoint | For Page |
|----------|-------------------|----------|
| HIGH | SystemSettingsController (GET/PUT) | SystemSettings.razor |
| HIGH | SubjectsController (GET/POST/PUT/DELETE) | Subjects.razor |
| MEDIUM | GET api/admin/documents (list) | Documents.razor |
| MEDIUM | GET api/admin/documents/{id} (detail) | DocumentDetail.razor |
| MEDIUM | GET api/admin/users/{id} (detail) | UserDetail.razor |
| LOW | GET api/dashboard/admin/attention-docs | Dashboard.razor |
| LOW | GET api/dashboard/admin/critical-events | Dashboard.razor |

---

## Progress List

### Phần 1: System Settings — HIGH (mock 100%, no backend)
1.1 Tạo SystemConfigService + interface
1.2 Tạo SystemSettingsController (GET/PUT api/admin/settings)
1.3 Connect SystemSettings.razor → real API

### Phần 2: Subjects — HIGH (mock 100%, no backend)
2.1 Tạo SubjectService + interface
2.2 Tạo SubjectsController (GET/POST/PUT/DELETE api/admin/subjects)
2.3 Connect Subjects.razor → real API

### Phần 3: Documents Admin — MEDIUM (mock 100%, missing GET)
3.1 Add GET api/admin/documents (list + filter/pagination)
3.2 Connect Documents.razor → real API
3.3 Add GET api/admin/documents/{id} (detail + chunks)
3.4 Connect DocumentDetail.razor → real API

### Phần 4: User Detail — MEDIUM (mock 100%, missing GET)
4.1 Add GET api/admin/users/{id}
4.2 Connect UserDetail.razor → real API

### Phần 5: Dashboard Polish — LOW (mixed)
5.1 Add attention-docs + critical-events endpoints
5.2 Connect attention table + timeline → real API
