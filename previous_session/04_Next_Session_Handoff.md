# Next Session Handoff - AI_Study_Hub_v2

> **STATUS: OBSOLETE (kept for history)**
> **Superseded by:** [`06_Session_2026-05-24_Build_Handoff.md`](./06_Session_2026-05-24_Build_Handoff.md)
> **For current state:** đọc `02_Resume_Pack.md` (đã refresh 2026-05-24).
> **For architecture:** đọc `01_Architecture_Reference.md` (đã refresh 2026-05-24).
>
> File này được viết ngày 2026-05-24 *trước* khi migration sang Supabase Local thực thi. Toàn bộ "next steps" (viết plan, execute migration, smoke test) đã làm xong trong session cùng ngày — chi tiết build log + deviations + known issues nằm ở file 06. Giữ file này nguyên trạng làm lịch sử quyết định.

---

**Ngày:** 2026-05-24 (kết thúc session hiện tại)  
**Người viết:** Claude (tóm tắt + handover cho session tiếp theo)  
**Mục tiêu session này:** Setup test infra + lock quyết định + chuẩn bị migration plan

---

## 1. Current State (Verified - End of Session)

### Running Services
- **Postgres**: `aistudyhub-db` healthy (`Up 12 hours`) → `localhost:5433` (5433→5432)
- **App**: `AI_Study_Hub_v2` đang chạy → `localhost:5240` (đã restart sau build)
- **Build status**: Solution build **succeeded** (0 error, 0 warning sau khi fix)

### Test Infrastructure (MỚI - Hoàn thành trong session này)
- **Project**: `AI_Study_Hub_v2.Tests` (NUnit 3.14.0 + FluentAssertions 6.12.1 + Moq 4.20.72 + Microsoft.AspNetCore.Mvc.Testing 8.0.10)
- Đã add vào solution `AI_Study_Hub_v2.sln`
- Đã exclude folder `AI_Study_Hub_v2.Tests/**` khỏi main project compile (trong `.csproj`)
- **SmokeTests.cs** (3 tests pass 100%):
  1. `TestRunner_Should_Be_Working` → 1+1 = 2
  2. `FluentAssertions_Should_Be_Available`
  3. `MainProject_Reference_Should_Compile` (dùng `AppDbContext`)
- `dotnet test` → **Passed! 3/3** (1.04s)

**Lưu ý**: Smoke tests **không test Auth logic** (sẽ thay thế sau migration).

---

## 2. Locked Decisions (Kiệt đã confirm ngày 24/5/2026)

| Quyết định | Nội dung | Ghi chú |
|------------|----------|--------|
| **A1** | Supabase Auth → **Local self-hosted** (full Docker stack) | Đã raise risk, Kiệt vẫn chọn Local |
| **A2** | Supabase Storage | OK (chưa code Phase 2 → không tốn rework) |
| **A3** | Bỏ hoàn toàn **Phase 3** + **Sub-RQ 3** (Citation accuracy) | Giữ focus FPT-specific |
| **B2** | Dùng **NUnit** từ đầu | Đã setup xong |
| **B6** | Chỉ dùng **Groq free tier** | Confirm rõ ràng |

---

## 3. Phase 1 Status (Pre-Migration - Vẫn còn Custom JWT)

**Đang chạy ổn định:**
- Custom JWT (HS256) + BCrypt 11 + EF Core 8
- Bảng: `roles`, `users` (có `password_hash`), `refresh_tokens`
- 5 endpoints `/api/auth/*` đã pass smoke test 10/10
- Services: `JwtTokenService`, `RefreshTokenService`, `PasswordHasher`, `AuthController`, `AuthService`

**Sẽ bị rip hoàn toàn** khi migrate sang Supabase Local.

---

## 4. Next Session - Thứ tự công việc khuyến nghị

### Bước 1: Viết chi tiết Migration Plan (30-45 phút)
Tạo file mới: `05_Supabase_Local_Migration_Plan.md`

**Nội dung cần có trong plan:**
- Docker compose cho Supabase Local (pin version, tương thích pgvector)
- Schema migration mới: `RipCustomAuth_AddSupabaseAuth`
  - `DROP TABLE refresh_tokens`
  - `ALTER TABLE users`:
    - Drop `password_hash`
    - Drop `email`
    - Thêm `supabase_user_id UUID` → FK `auth.users(id) ON DELETE CASCADE`
    - Giữ: `username`, `full_name`, `role_id`, `is_active`, `total_tokens_used`, timestamps
- Thay thế services:
  - Dùng Supabase .NET SDK (`supabase-csharp`) hoặc raw HTTP client (GoTrue + PostgREST)
- Blazor Server Auth flow (khuyến nghị: server-side token handling qua HttpContext)
- RLS policies (row level security)
- Connection string thay đổi
- Update Resume Pack Section 2 + Architecture_Reference.md

### Bước 2: Trình bày plan cho Kiệt review → Xin GO

### Bước 3: Execute Migration (mục tiêu hoàn thành + smoke test trước 26/5/2026)
- Tạo Supabase Local stack
- Chạy migration
- Viết `SupabaseAuthService` mới
- Update AuthController / endpoints
- Viết test mới cho auth flow (thay thế SmokeTests)
- Full smoke test 5 endpoints mới
- Update docs

### Bước 4 (song song hoặc sau):
- Bắt đầu Phase 2: Document + RAG + pgvector (trên Supabase Postgres)
- Tích hợp Groq (free tier)

---

## 5. Critical Warnings cho Session Tiếp Theo

1. **KHÔNG đụng code rip Phase 1** cho đến khi Kiệt confirm plan chi tiết.
2. Supabase Local stack khá nặng → cần hướng dẫn Kiệt cách `docker compose up -d` và kiểm tra health.
3. Port Postgres sẽ thay đổi (thường là 5432 trong stack Supabase Local).
4. Sau migration:
   - Xóa package `BCrypt.Net-Next` (nếu không dùng nữa)
   - Thêm package `supabase-csharp`
   - Update `Program.cs` / `appsettings.json`
5. Blazor Server → không nên dùng full Supabase JS SDK. Nên dùng server-side flow.

---

## 6. Files quan trọng cần tham khảo

- `previous_session/01_Architecture_Reference.md` → cần update sau migration
- `previous_session/02_Resume_Pack.md` → Section 2 cần cập nhật quyết định mới
- `previous_session/03_Prompt_Playbook.md`
- `Suggest_from_Claude/03_Solutions.md` (gốc)
- `AI_Study_Hub_v2.Tests/SmokeTests.cs` (sẵn sàng thay thế bằng test thật)

---

## 7. Status cuối session

- ✅ Test infrastructure: **Green** (3/3 pass)
- ✅ Quyết định: **Đã lock**
- ⏳ Migration plan: **Chưa viết**
- ⏳ Code rip + Supabase Local: **Chưa bắt đầu**
- App & DB: **Healthy**

---

**Kết luận cho session tiếp theo:**

> **Bắt đầu bằng việc viết file `05_Supabase_Local_Migration_Plan.md` chi tiết.**  
> Sau đó gửi cho Kiệt review → nhận GO → mới execute.

Session này đã hoàn thành phần setup test + lock quyết định.  
Session sau tập trung **100% vào migration plan + execution**.

---

**Hết file handoff**