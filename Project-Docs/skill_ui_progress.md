# Skill: UI-to-Progress — Auto Generate Backlog Từ UI

## 1. Purpose

Skill này dùng khi cần phân tích UI có sẵn → tự động sinh ra prioritized progress list để làm backend từ trên xuống từng phần 1.

Không code. Chỉ phân tích và xuất list.

## 2. When to Use

```text
- "Nhìn UI ra list việc cần làm"
- "Tạo backlog từ UI hiện có"
- "Phân tích UI rồi chia task"
- "Cái gì thiếu, làm từ đâu trước"
- "Ra progress list từ trên xuống"
```

## 3. Workflow

### Step 1 — Scan all admin pages
Duyệt từng file `.razor` trong `Components/Admin/`:
- Xác định page route, purpose
- List tất cả data fields hiển thị
- List tất cả actions (button, form, filter, search, modal)
- Check inject services / API clients

### Step 2 — Phân loại mỗi page
| Class | Dấu hiệu |
|-------|----------|
| 🔴 **Mock Data** | `BuildSeed*()`, `new List`, hardcoded values, không inject API client |
| 🟡 **Mixed** | Có API client nhưng fallback mock, hoặc 1 phần gọi API, 1 phần hardcode |
| 🟢 **Real API** | Inject `AuthSessionState` + API client, gọi qua `Session.AccessToken` |

### Step 3 — Map UI → Backend Requirement
Với mỗi UI element, suy ra backend cần gì:
- Table/card data → `GET` endpoint
- Form submit → `POST/PUT` endpoint
- Button action → `PATCH/DELETE` endpoint
- Search/filter → query params
- Pagination → `limit/offset` params

### Step 4 — Kiểm tra backend hiện có
- Đối chiếu với `Controllers/` hiện tại
- Xác định endpoint đã có hay chưa
- Nếu có: method/path/response có khớp UI không?

### Step 5 — Phân loại từng endpoint
```
Already works    — endpoint có sẵn, frontend đã connect
Needs connection — endpoint có sẵn, frontend chưa connect (mock data)
Missing backend  — frontend cần nhưng chưa có controller
Partially done   — có endpoint nhưng thiếu method (ví dụ: có list nhưng thiếu detail)
```

### Step 6 — Tạo Progress List
Xếp theo priority:

```text
Priority HIGH:
- Page chưa có backend controller nào → tạo từ đầu
- Page mock 100% → connect API

Priority MEDIUM:
- Page đã có backend 1 phần → thêm missing endpoint
- Page mixed → hoàn thiện phần còn mock

Priority LOW:
- Page đã real API → polish, thêm edge cases
```

## 4. Output Format

```markdown
## Admin Backend Progress List

### Phần 1: [Tên phần] — [Trạng thái hiện tại]
| # | Task | UI Page | Backend Status | Priority |
|---|------|---------|---------------|----------|
| 1.1 | ... | ... | Missing/Needs/Exists | HIGH |

### Phần 2: [Tên phần] — [Trạng thái hiện tại]
...
```

Sau khi xuất list, chờ user chọn phần để bắt đầu.

## 5. Safety Rules

- Không code — chỉ phân tích và xuất list
- Dùng evidence từ code thực tế, không đoán
- Nếu không chắc → ghi `[UNVERIFIED]`
- Ưu tiên đúng: HIGH (mock 100%) → MEDIUM (thiếu 1 phần) → LOW (polish)

## Relationship With Other Skills

**Foundation:**
- Follow `aistudy` for project context (stack, paths, commands)
- Follow `existing-project` Phase 2-5 for UI/backend analysis

**Should be used before:**
- `existing-project` — ra list trước rồi mới code từng phần
- `role-audit` — nếu list có liên quan đến role/permission

**Should be used after:**
- `aistudy` — cần biết project structure trước khi phân tích

**Should not replace:**
- `existing-project` — không code, chỉ phân tích
- `role-audit` — không so sánh role, chỉ map UI→backend
