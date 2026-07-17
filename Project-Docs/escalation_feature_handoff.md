# Escalation Feature — Full Work Log & Handoff

> **Ngày hoàn thành:** 2026-07-09  
> **Tác giả:** OpenCode Orchestrator  
> **Branch:** `main`  
> **Commit gốc:** `05dba22` (feat: add escalation UI) + amendments sau đó  
> **Mục đích:** Ghi lại toàn bộ quá trình phát triển chức năng Escalation (Moderator → Admin) từ đầu đến hiện tại, để bàn giao cho người phụ trách tiếp theo.

---

## Mục lục

1. [Tổng quan](#1-tổng-quan)
2. [Quá trình làm việc chi tiết](#2-quá-trình-làm-việc-chi-tiết)
3. [Danh sách file thay đổi](#3-danh-sách-file-thay-đổi)
4. [API Endpoints](#4-api-endpoints)
5. [Cấu trúc Database](#5-cấu-trúc-database)
6. [Các lỗi đã gặp và cách fix](#6-các-lỗi-đã-gặp-và-cách-fix)
7. [Hướng dẫn test](#7-hướng-dẫn-test)
8. [Công việc còn lại](#8-công-việc-còn-lại)

---

## 1. Tổng quan

### Mô tả chức năng

Chức năng **Escalation** cho phép **Moderator** gửi yêu cầu lên **Admin** khi moderator không đồng ý với kết quả moderation (document bị reject). Admin có thể xem, approve/reject escalation và gửi phản hồi lại moderator.

### Flow người dùng

```
┌─────────────────────────────────────────────────────────────┐
│  Moderator                                                  │
│  ┌──────────────────────┐    ┌──────────────────────────┐   │
│  │ /dashboard/documents │───→│ Reject document          │   │
│  └──────────────────────┘    └──────────┬───────────────┘   │
│                                         ↓                   │
│                              ┌──────────────────────────┐   │
│                              │ Nút "Escalate" (cam)     │   │
│                              │ xuất hiện trên document  │   │
│                              │ bị Rejected              │   │
│                              └──────────┬───────────────┘   │
│                                         ↓                   │
│                              ┌──────────────────────────┐   │
│                              │ Dialog: chọn document +  │   │
│                              │ nhập lý do → Gửi         │   │
│                              └──────────┬───────────────┘   │
│                                         ↓                   │
│                              ┌──────────────────────────┐   │
│                              │ /dashboard/escalations   │   │
│                              │ Xem lịch sử escalation  │   │
│                              │ + phản hồi admin         │   │
│                              └──────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                           │ POST /api/admin/escalations
                           ↓
┌─────────────────────────────────────────────────────────────┐
│  Admin                                                      │
│  ┌──────────────────────┐    ┌──────────────────────────┐   │
│  │ /admin/escalations   │───→│ Thấy escalation Pending  │   │
│  └──────────────────────┘    └──────────┬───────────────┘   │
│                                         ↓                   │
│                              ┌──────────────────────────┐   │
│                              │ Click "Resolve"          │   │
│                              │ → Dialog: Approved/      │   │
│                              │   Rejected + phản hồi    │   │
│                              └──────────┬───────────────┘   │
│                                         ↓                   │
│                              ┌──────────────────────────┐   │
│                              │ KPI "Resolved" tăng      │   │
│                              │ Card hiển thị status     │   │
│                              │ RESOLVED + phản hồi      │   │
│                              └──────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. Quá trình làm việc chi tiết

### Phase 1: Khởi tạo (Commit `05dba22`)

**Yêu cầu ban đầu:** Triển khai UI Escalation (Moderator → Admin)

**Những gì đã làm:**
1. Đọc codebase để hiểu escalation flow có sẵn: `EscalationService`, `EscalationController`, `EscalationApiClient`, DTOs
2. Đăng ký `EscalationApiClient` trong DI (`Program.cs`)
3. Thêm nút "Escalate to Admin" vào `DocumentModeration.razor` (admin area):
   - Trong inspection panel bên phải (viền cam)
   - Trong menu 3-chấm của table (chỉ enabled khi document Rejected)
4. Thêm CSS `.escalate-btn` (viền cam)
5. Tạo `EscalationDialog.razor` trong `Components/Admin/Documents/` — popup chọn document bị reject + lý do
6. Tạo `AdminEscalations.razor` — trang `/admin/escalations` với KPI cards + escalation cards + nút Resolve
7. Tạo `ResolveEscalationDialog.razor` — popup approve/reject kèm admin response
8. Thêm sidebar link + breadcrumb trong `AdminLayout.razor`
9. Build 0 errors, commit+push `05dba22`

**Vấn đề phát hiện sau đó:** UI đặt sai chỗ — moderator không phải admin, admin không thể escalate cho chính mình.

---

### Phase 2: Sửa UI sai chỗ (Moderator ≠ Admin)

**Yêu cầu:** "Nút Escalate + EscalationDialog phải ở Pages/Dashboard cho moderator, không phải admin"

**Những gì đã làm:**
1. Tạo `EscalationDialog.razor` mới trong `Components/Pages/Dashboard/` (namespace `Pages.Dashboard`)
2. Tạo `ModeratorEscalations.razor` — trang `/dashboard/escalations` cho moderator xem escalation history (KPI cards + danh sách cards)
3. Sửa `DocumentModeration.razor` (admin) — gọi `EscalationDialog` từ namespace `Pages.Dashboard`
4. Thêm sidebar link "Escalations" trong `DashboardLayout.razor`
5. Giữ nguyên `AdminEscalations.razor` + `ResolveEscalationDialog.razor` trong Admin (admin vẫn cần resolve)

---

### Phase 3: Chuyển nút Escalate từ Admin sang Moderator

**Yêu cầu:** Xóa nút Escalate khỏi admin DocumentModeration, thêm vào moderator DocumentDashboard

**Những gì đã làm:**
1. **Xóa khỏi `DocumentModeration.razor` (admin):**
   - Xóa inject `EscalationApiClient`
   - Xóa menu item "Escalate to Admin" trong 3-chấm
   - Xóa nút escalate-btn trong inspection panel
   - Xóa method `OpenEscalationDialog()` + toàn bộ logic
   - Xóa CSS `.escalate-btn` + hover
2. **Thêm vào `DocumentDashboard.razor` (moderator):**
   - Thêm inject `IDialogService` + `EscalationApiClient`
   - Thêm nút **Escalate** (viền cam, icon Flag) bên cạnh nút Approve/Reject — **chỉ hiện khi document Rejected**
   - Thêm method `OpenEscalationDialogForDocument()` — gọi dialog + API

---

### Phase 4: Fix lỗi Forbidden (Moderator không gọi được API)

**Lỗi:** Moderator gọi `GET /api/admin/escalations` bị 403 Forbidden

**Nguyên nhân:** Endpoint `GET /api/admin/escalations` chỉ cho phép `[Authorize(Roles = "Admin")]`

**Những gì đã làm:**
1. **EscalationService.cs** — thêm `GetMyAsync(Guid userId)` — lọc escalation theo `EscalatedByUserId`
2. **EscalationController.cs** — thêm `GET /api/admin/escalations/my` với `[Authorize(Roles = "Admin,Moderator")]`
3. **EscalationApiClient.cs** — thêm `GetMyAsync(string accessToken)`
4. **ModeratorEscalations.razor** — đổi từ `GetPendingAsync` → `GetMyAsync`

---

### Phase 5: Fix lỗi FK constraint `folder_id`

**Lỗi:** `insert or update on table "document_escalations" violates foreign key constraint "FK_document_escalations_folders_folder_id"`

**Nguyên nhân:** Gửi `FolderId = Guid.Empty` không tồn tại trong bảng `folders`

**Fix:** Sửa `OpenEscalationDialogForDocument` trong `DocumentDashboard.razor`:
- Lấy `folderId` thật từ `document.FolderId`
- Chỉ lọc rejected documents cùng folder đó
- Gửi `folderId` thật trong request

---

### Phase 6: Fix lỗi FK constraint `escalated_by_user_id`

**Lỗi:** `violates foreign key constraint "FK_document_escalations_users_escalated_by_user_id"`

**Nguyên nhân:** JWT claim trả về `SupabaseUserId` (từ Supabase Auth), nhưng FK `escalated_by_user_id` trỏ đến `User.Id` (PK local). Hai giá trị này khác nhau.

**Fix:** Sửa `EscalationController`:
- Inject `AppDbContext`
- Thêm method `GetSupabaseUserIdAsync()` — lấy `SupabaseUserId` từ JWT claim, lookup `User.Id` từ DB qua `u.SupabaseUserId == supabaseUserId`
- Cả `Create` và `GetMy` đều dùng method này

---

### Phase 7: Fix resolved count không hiển thị

**Lỗi:** Sau khi admin resolve escalation, KPI card "Resolved" vẫn = 0

**Nguyên nhân:** `AdminEscalations.razor` gọi `GetPendingAsync()` — chỉ trả về escalations có status `"Pending"`. Sau resolve status thành `"Approved"`/`"Rejected"` → không còn trong danh sách.

**Những gì đã làm:**
1. **EscalationService.cs** — thêm `GetAllAsync()` — trả về tất cả escalations (không filter status)
2. **EscalationController.cs** — thêm `GET /api/admin/escalations/all` (chỉ Admin)
3. **EscalationApiClient.cs** — thêm `GetAllAsync()`
4. **AdminEscalations.razor** — đổi từ `GetPendingAsync` → `GetAllAsync`

---

## 3. Danh sách file thay đổi

### File tạo mới (3 files)

| # | File | Đường dẫn | Mô tả |
|---|------|-----------|-------|
| 1 | `EscalationDialog.razor` | `Components/Pages/Dashboard/` | Dialog tạo escalation: chọn document bị reject + nhập lý do. Namespace `Pages.Dashboard` |
| 2 | `ModeratorEscalations.razor` | `Components/Pages/Dashboard/` | Trang `/dashboard/escalations`: KPI cards (Pending/Resolved) + danh sách escalation cards + phản hồi admin |
| 3 | `ResolveEscalationDialog.razor` | `Components/Admin/Documents/` | Dialog resolve: chọn Approved/Rejected + nhập admin response (giữ nguyên từ commit gốc) |

### File đã sửa (8 files)

| # | File | Thay đổi chính |
|---|------|----------------|
| 1 | `Components/Admin/Documents/DocumentModeration.razor` | **Xóa:** inject `EscalationApiClient`, menu item "Escalate", nút escalate-btn, method `OpenEscalationDialog()`, CSS `.escalate-btn` |
| 2 | `Components/Pages/Dashboard/DocumentDashboard.razor` | **Thêm:** inject `IDialogService` + `EscalationApiClient`, nút Escalate (cam, chỉ hiện khi Rejected), method `OpenEscalationDialogForDocument()` với FolderId thật |
| 3 | `Components/Admin/Documents/AdminEscalations.razor` | Đổi `GetPendingAsync` → `GetAllAsync` để hiển thị cả resolved |
| 4 | `Components/Layout/DashboardLayout.razor` | Thêm sidebar item "Escalations" (icon Flag) → `/dashboard/escalations` |
| 5 | `Components/Admin/Shared/AdminLayout.razor` | Thêm sidebar link + breadcrumb cho Escalations (từ commit gốc) |
| 6 | `Controllers/EscalationController.cs` | Inject `AppDbContext`, thêm `GetSupabaseUserIdAsync()` (lookup User.Id từ SupabaseUserId), thêm endpoint `GET /all` + `GET /my` |
| 7 | `Services/EscalationService.cs` | Thêm `GetAllAsync()` (không filter) + `GetMyAsync(Guid userId)` (filter theo user) |
| 8 | `Services/EscalationApiClient.cs` | Thêm `GetAllAsync()` + `GetMyAsync()` |

---

## 4. API Endpoints

| Method | Endpoint | Auth | Mô tả |
|--------|----------|------|-------|
| POST | `/api/admin/escalations` | Admin, Moderator | Tạo escalation mới |
| GET | `/api/admin/escalations` | Admin | Lấy tất cả pending escalations |
| GET | `/api/admin/escalations/all` | Admin | Lấy tất cả escalations (cả pending + resolved) |
| GET | `/api/admin/escalations/my` | Admin, Moderator | Lấy escalation của user hiện tại (theo User.Id) |
| PATCH | `/api/admin/escalations/{id}` | Admin | Resolve escalation (update status + admin response) |

### Chi tiết Authorization

- **Class level:** `[Authorize(Roles = "Admin,Moderator")]`
- **Override từng endpoint:**
  - `GET /` → chỉ Admin
  - `GET /all` → chỉ Admin
  - `PATCH {id}` → chỉ Admin
  - `POST /` → Admin, Moderator
  - `GET /my` → Admin, Moderator

### Lưu ý quan trọng về User ID

Controller inject `AppDbContext` để lookup `User.Id` từ `SupabaseUserId` (JWT sub claim):

```csharp
private async Task<Guid?> GetSupabaseUserIdAsync()
{
    var supabaseUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
    if (supabaseUserIdClaim is null || !Guid.TryParse(supabaseUserIdClaim.Value, out var supabaseUserId))
        return null;
    var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);
    return user?.Id;
}
```

Lý do: `DocumentEscalation.EscalatedByUserId` là FK trỏ đến `User.Id` (PK local), nhưng JWT claim từ Supabase Auth trả về `SupabaseUserId` — hai giá trị khác nhau trong cùng bảng `users`.

---

## 5. Cấu trúc Database

### Table: `document_escalations`

```sql
CREATE TABLE document_escalations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    folder_id UUID NOT NULL REFERENCES folders(id) ON DELETE CASCADE,
    escalated_by_user_id UUID NOT NULL REFERENCES users(id) ON DELETE NO ACTION,
    reason VARCHAR(2000) NOT NULL,
    escalation_status VARCHAR(50) NOT NULL DEFAULT 'Pending',
    admin_response VARCHAR(2000),
    resolved_by_user_id UUID REFERENCES users(id) ON DELETE NO ACTION,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    resolved_at TIMESTAMPTZ
);

CREATE INDEX idx_document_escalations_folder_id ON document_escalations(folder_id);
CREATE INDEX idx_document_escalations_status ON document_escalations(escalation_status);
```

### Table: `document_escalation_items`

```sql
CREATE TABLE document_escalation_items (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    escalation_id UUID NOT NULL REFERENCES document_escalations(id) ON DELETE CASCADE,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE NO ACTION,
    reject_reason VARCHAR(2000) NOT NULL
);

CREATE INDEX idx_document_escalation_items_escalation_id ON document_escalation_items(escalation_id);
```

### Entity Models

**DocumentEscalation:**
- `Id` (Guid), `FolderId` (Guid, required), `EscalatedByUserId` (Guid, required)
- `Reason` (string, max 2000), `EscalationStatus` (string, default "Pending")
- `AdminResponse` (string?, max 2000), `ResolvedByUserId` (Guid?)
- `CreatedAt` (DateTimeOffset), `ResolvedAt` (DateTimeOffset?)
- Navigation: `Folder`, `EscalatedByUser`, `ResolvedByUser`, `Items`

**DocumentEscalationItem:**
- `Id` (Guid), `EscalationId` (Guid), `DocumentId` (Guid)
- `RejectReason` (string, max 2000)
- Navigation: `Escalation`, `Document`

---

## 6. Các lỗi đã gặp và cách fix

### Lỗi #1: UI escalation đặt trong admin area

- **Triệu chứng:** Moderator không tìm thấy nút "Escalate to Admin"
- **Nguyên nhân:** Nút + dialog được tạo trong `Components/Admin/Documents/` — admin escalate cho chính mình là sai logic
- **Fix:** Tạo bản sao dialog trong `Components/Pages/Dashboard/`, chuyển nút sang `DocumentDashboard.razor`, xóa khỏi admin

### Lỗi #2: 403 Forbidden khi moderator gọi GET escalations

- **Triệu chứng:** Trang `/dashboard/escalations` hiển thị "Unable to load escalations: Request failed (Forbidden)"
- **Nguyên nhân:** `GET /api/admin/escalations` chỉ cho phép role Admin
- **Fix:** Thêm endpoint `GET /api/admin/escalations/my` cho cả Admin, Moderator — lọc theo `EscalatedByUserId`

### Lỗi #3: FK constraint `folder_id` = Guid.Empty

- **Triệu chứng:** `23503: insert or update on table "document_escalations" violates foreign key constraint "FK_document_escalations_folders_folder_id"`
- **Nguyên nhân:** Gửi `FolderId = Guid.Empty` không tồn tại trong bảng `folders`
- **Fix:** Lấy `FolderId` thật từ `document.FolderId` (nullable Guid), kiểm tra null trước khi gửi

### Lỗi #4: FK constraint `escalated_by_user_id` không match

- **Triệu chứng:** `23503: insert or update on table "document_escalations" violates foreign key constraint "FK_document_escalations_users_escalated_by_user_id"`
- **Nguyên nhân:** JWT claim trả về `SupabaseUserId`, FK trỏ đến `User.Id` — khác nhau
- **Fix:** Inject `AppDbContext` vào controller, lookup `User.Id` từ `SupabaseUserId`

### Lỗi #5: Resolved count luôn = 0

- **Triệu chứng:** Sau khi resolve, KPI card "Resolved" vẫn hiển thị 0
- **Nguyên nhân:** `GetPendingAsync()` chỉ trả về escalations có status "Pending"
- **Fix:** Thêm `GetAllAsync()` + endpoint `/all`, đổi AdminEscalations dùng nó

---

## 7. Hướng dẫn test

### Yêu cầu

- 2 tài khoản: 1 Moderator + 1 Admin (đã có trong DB với `SupabaseUserId` hợp lệ)
- App đã chạy, migration đã chạy

### Test flow hoàn chỉnh

```powershell
# Build trước khi run (không --no-build)
dotnet build "AI_Study_Hub_v2\AI_Study_Hub_v2.sln" --nologo
dotnet run --project "AI_Study_Hub_v2\AI_Study_Hub_v2"
```

**Bước 1 — Moderator tạo escalation**
1. Login Moderator → vào `/dashboard/documents`
2. Chọn 1 document → click **Reject** (nút đỏ)
3. Sau khi reject, nút **Escalate** (viền cam, icon Flag) xuất hiện
4. Click **Escalate** → dialog hiện ra với danh sách document bị reject
5. Chọn document + nhập lý do → click **Escalate to Admin**
6. Snackbar: "Escalation created with N document(s)"

**Bước 2 — Admin resolve**
1. Login Admin → `/admin/escalations`
2. Thấy escalation mới với status **PENDING**, có nút **Resolve**
3. KPI card "Pending" = 1, "Resolved" = 0
4. Click **Resolve** → dialog hiện escalation reason + dropdown Decision
5. Chọn **Approved** (hoặc Rejected) + nhập phản hồi → click **Resolve**
6. Snackbar: "Escalation resolved successfully."
7. KPI "Resolved" tăng lên 1, card chuyển sang status **RESOLVED** + hiển thị admin response

**Bước 3 — Moderator kiểm tra**
1. Login Moderator → sidebar **Escalations** (hoặc `/dashboard/escalations`)
2. Thấy escalation đã resolved + phản hồi của admin

### Checklist edge cases

- [ ] **Escalate khi không có document Rejected** → snackbar "No rejected documents to escalate"
- [ ] **Escalate với document không có FolderId** → snackbar "Document has no folder assigned"
- [ ] **Admin resolve với Approved** → status "APPROVED", admin response hiển thị
- [ ] **Admin resolve với Rejected** → status "REJECTED", admin response hiển thị
- [ ] **Moderator không thấy escalation của moderator khác** (GetMyAsync filter)
- [ ] **Admin thấy tất cả escalations** (GetAllAsync không filter)
- [ ] **Refresh trang** → dữ liệu vẫn đúng (load lại từ API)

---

## 8. Công việc còn lại

### Gợi ý cải tiến (theo thứ tự ưu tiên)

| # | Công việc | Mô tả | Ưu tiên |
|---|----------|-------|---------|
| 1 | **Auto-update document status** | Khi admin approve escalation, tự động set document về "Pending Review" để moderator review lại | Cao |
| 2 | **Notification** | Gửi thông báo real-time (SignalR) cho moderator khi escalation được resolve | Cao |
| 3 | **Audit log** | Ghi lại ai escalate, ai resolve, lý do, thời gian vào bảng audit | Trung bình |
| 4 | **Filter/Search** | Thêm filter theo status, date range, moderator name cho admin page | Trung bình |
| 5 | **Pagination** | Cho cả admin và moderator page khi có nhiều escalations | Thấp |
| 6 | **Unit tests** | Test cho EscalationService, EscalationController, EscalationApiClient | Thấp |

### Lưu ý kỹ thuật cho người tiếp theo

1. **User ID mapping:** Luôn nhớ lookup `User.Id` từ `SupabaseUserId` khi làm việc với JWT claims
2. **FolderId required:** `DocumentEscalation.FolderId` là required FK — không thể gửi Guid.Empty
3. **Authorization phân tầng:** Class-level `[Authorize(Roles = "Admin,Moderator")]` + override từng endpoint
4. **Build trước khi run:** `dotnet build` rồi mới `dotnet run` (không `--no-build`) vì DLL cũ
5. **File markdown này:** Lưu tại `previous_session/escalation_feature_handoff.md`