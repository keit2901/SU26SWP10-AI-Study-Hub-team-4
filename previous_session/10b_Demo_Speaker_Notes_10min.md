# 10b — Speaker Notes — Demo NUnit (bản 10 phút)

> **Mục đích:** đoạn nói chính xác cho buổi demo NUnit, cắt gọn từ `10_Demo_Speaker_Notes.md` (bản 12p) xuống ≤10 phút vì em là người cuối trong bài thuyết trình 4 người.
> **Demo case:** Service layer (`RegisterAsync_HappyPath`) + Controller layer (`Login_WhenServiceThrowsAuthException`).
> **Tổng thời lượng:** ~9 phút speaker note + ~1 phút buffer (chuyển slide + đợi build) = 10 phút.
> **Author:** OpenCode (kr/claude-opus-4.7) — 2026-05-25
> **Khác bản 12p:** cắt Slide 7 cũ (TestDb helper), gộp intro+input của mỗi demo vào 1 slide, trim mở đầu/closing/why-NUnit ~50%, **thêm Slide 8 break-then-fix** để chứng minh test thực sự verify business rule.

---

## 0. Cách dùng file này

- Mỗi slide có 3 phần: **[Cap màn hình]**, **[Hiện slide]**, **[Speaker note]**.
- `[Speaker note]` viết theo lối nói tự nhiên — đọc nguyên văn được.
- Đoạn `*nghiêng*` là hint cho người trình bày, không đọc thành tiếng.
- `⏱` là thời gian gợi ý. `[≈ phút X]` là checkpoint — đọc đến đây thì đang ở phút thứ X của 10 phút.

---

## Slide 1 — Mở đầu (⏱ 25s) — `[≈ 0:25]`

### [Hiện slide]
- **Demo: Unit Test với NUnit cho AI_Study_Hub_v2**
- ASP.NET Core 8 + Blazor Server + Supabase Local + EF Core
- **38/38 test pass — chạy offline, không cần Docker**

### [Speaker note]
> Em chào thầy/cô và các bạn. Phần em là demo unit test cho project bằng NUnit. Bộ test hiện tại có 38 case, tất cả pass, và điểm em muốn nhấn mạnh ngay: **chạy hoàn toàn offline** — không cần Docker, không cần Postgres, không cần mạng. Em đi qua 3 phần: cài đặt, demo 1 test ở tầng service, demo 1 test ở tầng controller, rồi chạy live.

---

## Slide 2 — Tại sao chọn NUnit (⏱ 25s) — `[≈ 0:50]`

### [Hiện slide]
- 3 framework: **NUnit / xUnit / MSTest** — sức mạnh ngang nhau
- Team chọn **NUnit 3.14**:
  - Cú pháp gần JUnit (cả lớp đã học Java)
  - Setup/Teardown rõ ràng qua attribute
  - Tài liệu Việt nhiều

### [Speaker note]
> .NET có 3 framework test phổ biến — NUnit, xUnit, MSTest — sức mạnh gần như ngang nhau. Team chọn NUnit 3.14 vì cú pháp gần JUnit của Java mà cả lớp đã học, Setup/Teardown rõ ràng qua attribute thay vì pattern Constructor/Dispose của xUnit, và tài liệu tiếng Việt nhiều hơn.

---

## Slide 3 — Cài đặt (⏱ 45s) — `[≈ 1:35]`

### [Cap màn hình]
- File `AI_Study_Hub_v2.Tests.csproj` (line 1-33), zoom ~14pt, line numbers ON.

### [Hiện slide]
- 2 cách setup, kết quả như nhau:
  - **Terminal (3 lệnh):**
    1. `dotnet new nunit -n AI_Study_Hub_v2.Tests -f net8.0`
    2. `dotnet sln add` + `dotnet add reference` (link main project)
    3. `dotnet add package` — FluentAssertions, Moq, EFCore.InMemory
  - **Visual Studio GUI (click-by-click):** xem Phụ lục B
- 3 package lõi: `NUnit` + `NUnit3TestAdapter` + `NUnit.Analyzers`

### [Speaker note]
> Đây là csproj của test project. Em highlight 2 chỗ.
>
> Thứ nhất, dòng 19 đến 21, ba package lõi NUnit: `NUnit` là framework, `NUnit3TestAdapter` là cầu nối để runner discover được test, `NUnit.Analyzers` báo warning ngay trong VS khi viết test sai cú pháp.
>
> Thứ hai, ba package phụ trợ: `FluentAssertions` để viết assertion đọc như tiếng Anh — `result.Should().Be(2)`. `Moq` để mock interface. `EFCore.InMemory` để DbContext chạy trong RAM, không cần Postgres thật.
>
> Setup có 2 cách. Cách nhanh là 3 lệnh terminal. Cách dùng GUI Visual Studio cũng được — click chuột phải solution chọn Add New Project, chọn template **NUnit Test Project**, rồi mở **NuGet Package Manager** để cài 3 package phụ trợ. Cả 2 cách ra cùng kết quả csproj này. Em để chi tiết click-by-click ở phụ lục.

---

## Slide 4 — Demo 1: `RegisterAsync_HappyPath` — Setup (⏱ 60s) — `[≈ 2:35]`

### [Cap màn hình]
- File `Services/SupabaseAuthServiceTests.cs` line 46-76 (Arrange + Act).

### [Hiện slide]
- **Service layer** — verify luồng đăng ký user:
  - Tạo identity ở Supabase GoTrue
  - Mirror profile vào `public.users`
  - Trả access token + role Student
- **Input cố tình "bẩn":**
  - `Email = "  ALICE@aistudyhub.local  "` (whitespace + UPPERCASE)
  - `FullName = "  Alice A.  "`
- **Mock GoTrue chỉ trả session khi nhận data đã normalize**
- Không đụng Postgres / GoTrue thật — mọi dependency bị mock

### [Speaker note]
> Test đầu tiên ở tầng Service, tên `RegisterAsync_HappyPath`. Nó verify luồng đăng ký user — gồm 3 việc: tạo identity bên GoTrue, mirror profile vào bảng `public.users`, trả access token kèm role Student.
>
> Điểm thú vị ở slide này là input em cố tình thiết kế **bẩn**: email có khoảng trắng đầu cuối lại còn HOA, FullName cũng có khoảng trắng. Đây là dữ liệu giả lập user gõ tay — có thể Ctrl+V dính space, có thể caps lock.
>
> Mock GoTrue em setup `MockBehavior.Strict`, **chỉ trả về session khi service gọi với email đã trim và lowercase**, FullName đã trim. Nếu service gọi mock với data raw → mock không match → test fail. Đây là cách dùng mock để **ép service phải normalize đúng**, không chỉ check output.
>
> DB thì pre-seed sẵn 2 role Admin và Student. Cả test này không đụng Postgres thật, không đụng GoTrue thật.

---

## Slide 5 — Demo 1: Given-When-Then (⏱ 60s) — `[≈ 3:35]`

### [Cap màn hình]
- File `Services/SupabaseAuthServiceTests.cs` line 77-98 (3 nhóm Assert).

### [Hiện slide]
- **Given** — DB seed 2 role; mock GoTrue ready (Strict)
- **When** — `RegisterAsync(input bẩn)`
- **Then** — verify 3 nhóm:
  1. **Response** — token + email normalized + role Student
  2. **DB persist** — 1 row, link `SupabaseUserId`, role Student
  3. **Mock interaction** — `gotrue.VerifyAll()` ép service gọi với data đã normalize

### [Speaker note]
> Scenario theo cú pháp Given-When-Then.
>
> **Given** — DB in-memory seed sẵn 2 role; mock GoTrue ready trả session khi service gọi đúng cách.
>
> **When** — em gọi `RegisterAsync` với input bẩn vừa show.
>
> **Then** em verify 3 nhóm theo pattern AAA. Nhóm một, **response trả client**: token đúng giá trị mock, email và FullName đã normalize, role là Student, isActive là true. Nhóm hai, **side effect ở DB**: đúng 1 row trong `public.users`, link đúng `SupabaseUserId`, role Student, `TotalTokensUsed = 0`. Nhóm ba — quan trọng nhất — **mock interaction**: `gotrue.VerifyAll()`. Vì setup Strict, nếu service gọi mock với args khác setup → throw exception. Đây là cách dùng mock để **ép quy tắc business**.
>
> Test này KHÔNG cần Docker, KHÔNG cần Postgres, KHÔNG cần network. Chạy 30 mili giây.

---

## Slide 6 — Demo 2: `Login_WhenServiceThrowsAuthException` — Setup (⏱ 50s) — `[≈ 4:25]`

### [Cap màn hình]
- File `Controllers/AuthControllerTests.cs` line 114-130.

### [Hiện slide]
- **Controller layer** — verify HTTP contract khi service throw
- **Tình huống:** user gõ sai password → service ném `AuthException(401, "invalid_credentials", ...)`
- **Câu hỏi:** controller map đúng status + body shape không?
- **Mock IAuthService:** `ThrowsAsync(new AuthException(401, ...))`
- HttpContext anonymous (Login là `[AllowAnonymous]`)

### [Speaker note]
> Test thứ hai ở tầng cao hơn — Controller. Tên `Login_WhenServiceThrowsAuthException`.
>
> Tình huống: user gõ sai password, service tầng dưới ném `AuthException` với status 401 và error code `"invalid_credentials"`. Câu hỏi: **controller xử lý exception đó ra sao?** Có map đúng thành response 401 với body chuẩn không, hay quên try-catch để bubble lên thành 500?
>
> Em mock `IAuthService` dùng `ThrowsAsync` để giả lập service ném exception. HttpContext build qua helper, không cần user vì Login là anonymous endpoint. Test này **không đụng business logic, không đụng DB** — chỉ test HTTP contract.

---

## Slide 7 — Demo 2: Given-When-Then (⏱ 50s) — `[≈ 5:15]`

### [Hiện slide]
- **Given** — controller + mock service sẽ throw 401; HttpContext anonymous
- **When** — `sut.Login(...)` → service throw → controller catch + map
- **Then** — verify 3 thứ:
  1. Result type = `ObjectResult` (không Ok, không bubble)
  2. Status code = `401`
  3. Body = `ApiErrorResponse { Code = "invalid_credentials", Message = "..." }`

### [Speaker note]
> Given-When-Then cho test này.
>
> **Given** — controller build với mock service đã setup ném `AuthException(401)`.
>
> **When** — em gọi `Login` của controller. Bên trong nó gọi service, mock ném exception. Câu hỏi: controller xử lý ra sao?
>
> **Then** em verify 3 tầng. Một, **action result type** là `ObjectResult` — không phải Ok vì không success, cũng không được bubble vì runtime sẽ trả 500. Hai, **status code 401** lấy từ `AuthException.StatusCode` — service quyết, controller pass-through. Ba, **body shape** là `ApiErrorResponse`, có `Code = "invalid_credentials"` để client xử lý logic không cần parse message tiếng Anh, và `Message` để hiển thị cho user.
>
> Test này verify **hợp đồng HTTP** giữa server và client. Sau này ai sửa controller, vô tình swallow exception hay đổi format body → test fail ngay, bắt được lỗi trước khi merge.

---

## Slide 8 — Break-then-fix: chứng minh test thực sự verify business rule (⏱ 120s) — `[≈ 7:15]`

### [Chuẩn bị trước buổi — không show cho khán giả]
- Mở **2 file song song** trong VS (split vertical):
  - Trái: `AI_Study_Hub_v2/Services/SupabaseAuthService.cs` — cuộn sẵn tới line 44
  - Phải: `AI_Study_Hub_v2.Tests/Services/SupabaseAuthServiceTests.cs` — line 47-98
- **Pin Test Explorer** ở dock dưới, đã filter `RegisterAsync_HappyPath`.
- **Zoom editor 16-18pt**, theme Light hoặc Solarized cho slide rõ.
- Tắt notifications: `Win+A` → Focus assist → Alarms only.
- Tắt Discord, Telegram, mail; cắm sạc; resolution 1920×1080 (không 4K).
- **Backup an toàn:** copy nguyên folder project ra `demo_backup/` phòng `Ctrl+Z` không undo được.
- Tập kịch bản này **2 lần ở nhà**, đo thời gian — phải ≤120s.

### [Thao tác trên slide / IDE]
1. Chuyển sang VS, file `SupabaseAuthService.cs:44`. Highlight dòng:
   ```csharp
   var email = request.Email.Trim().ToLowerInvariant();
   ```
2. Sửa thành:
   ```csharp
   var email = request.Email;
   ```
   Save (`Ctrl+S`).
3. Test Explorer → chuột phải `RegisterAsync_HappyPath` → **Run** (KHÔNG Run All — chỉ 1 test cho nhanh).
4. Đợi ~1 giây, test fail. **Click vào test fail** để hiện Output panel với log Moq.
5. Highlight đoạn log:
   ```
   Moq.MockException : IGoTrueClient.SignUpAsync(...)
   invocation failed with mock behavior Strict.
   All invocations on the mock must have a corresponding setup.
   ```
6. `Ctrl+Z` để undo, save lại. Run lại test → pass.

### [Hiện slide]
- **Demo:** xóa `.Trim().ToLowerInvariant()` ở `SupabaseAuthService.cs:44`
- **Kết quả:** test fail vì mock `MockBehavior.Strict` không match args
- Log Moq chỉ rõ: service gọi với email **raw** thay vì email **đã normalize**
- Sửa lại → pass — chứng minh test ép quy tắc business, không fake-pass

### [Speaker note]
> Để chứng minh test này thực sự verify business rule chứ không phải fake-pass, em làm thử nghiệm tại chỗ. Trong service, dòng 44, em có code `request.Email.Trim().ToLowerInvariant()` để normalize email trước khi gọi GoTrue.
>
> *Thao tác: chuyển sang VS, xóa `.Trim().ToLowerInvariant()`, save.*
>
> Em xóa đoạn này đi, chỉ giữ `request.Email` thô. Save. Bây giờ service không còn normalize.
>
> *Thao tác: chuột phải `RegisterAsync_HappyPath` → Run.*
>
> Em chạy lại đúng test đó.
>
> *Đợi ~1s. Test fail.*
>
> Test fail. Nhìn log Moq: `MockException — invocation failed with mock behavior Strict`. Nghĩa là service đã gọi `SignUpAsync` với args mà mock **không setup** — cụ thể là email raw `"  ALICE@aistudyhub.local  "` thay vì email đã normalize `"alice@aistudyhub.local"`. Mock Strict không match → throw exception → test fail.
>
> Đây là điểm quan trọng nhất em muốn show: **test không chỉ check output cuối cùng, nó còn ép service phải gọi dependency đúng cách**. Nếu lập trình viên sau này vô tình xóa đoạn normalize, test sẽ fail ngay trên CI, không cho merge.
>
> *Thao tác: Ctrl+Z, save, Run lại.*
>
> Em sửa lại — `Ctrl+Z`. Save. Run lại. Pass. Test thực sự verify behavior.

*Nếu vượt 2 phút → bỏ phần `Ctrl+Z` cuối, để màn hình ở trạng thái fail, nói "em sẽ revert sau buổi" và chuyển slide tiếp.*
*Nếu Ctrl+Z không undo được → mở terminal, `git checkout AI_Study_Hub_v2/Services/SupabaseAuthService.cs` (cần repo có git), hoặc copy lại từ `demo_backup/`.*

---

## Slide 9 — Live run toàn bộ 38 test (⏱ 30s) — `[≈ 7:45]`

### [Thao tác]
1. Test Explorer → click **Run All** (clear filter trước nếu có)
2. Đợi ~2-3s, cap màn hình 38 icon xanh
3. *Optional:* mở Terminal, paste `dotnet test AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/AI_Study_Hub_v2.Tests.csproj --nologo`

### [Hiện slide]
- 38/38 pass — duration ~2 giây
- Cover: 3 sanity + 18 service (5 luồng auth × happy/error) + 17 controller
- Chạy offline — không cần Docker / Postgres / network

### [Speaker note]
> Bây giờ em Run All toàn bộ 38 test cho thầy/cô thấy bộ test đầy đủ.
>
> *Đợi run xong, chỉ vào màn hình.*
>
> 38 test pass, 2 giây. Chia 3 fixture: 3 sanity, 18 service cover 5 luồng auth — Register, Login, Refresh, Logout, Me — cả happy lẫn error path; 17 controller cover claim parsing, exception mapping, Bearer header. Toàn bộ chạy offline, không cần Docker hay Postgres.

---

## Slide 10 — Closing (⏱ 15s) — `[≈ 8:00 + buffer ≈ 10:00]`

### [Hiện slide]
- 38 test, 100% pass — verify cả Service + Controller
- Offline-first, chạy trên mọi CI/CD
- Tools: NUnit + FluentAssertions + Moq + EF Core InMemory

### [Speaker note]
> Tổng kết. 38 test, 100% pass, cover cả business logic ở service và HTTP contract ở controller. Offline-first nên chạy được trên mọi CI/CD. Phase 2 sắp tới về Document Management và RAG, bộ test sẽ mở rộng cho chunking, embedding, vector search. Em xin hết. Cảm ơn thầy/cô và các bạn.

---

## Phụ lục — Q&A đáp án ngắn

**Q: Tại sao NUnit thay vì xUnit?**
> Cú pháp gần JUnit, đa số đã học Java. Setup/Teardown qua attribute rõ hơn pattern Constructor/Dispose của xUnit. Sức mạnh ngang nhau.

**Q: Sao không test Postgres thật?**
> Đây là **unit test**, mục tiêu isolate business logic. Test Postgres thật là **integration test** — phase sau, đã quyết skip cho phase 1 do ROI thấp.

**Q: Mock vs Stub vs Fake?**
> Stub trả giá trị hard-code. Mock có verify call (số lần, args). Fake là implementation thật nhưng đơn giản. Demo em dùng cả 3 — mock cho IGoTrueClient, stub cho IAuthenticationService, fake là DbContext InMemory.

**Q: Coverage bao nhiêu phần trăm?**
> Phase 1 chưa đo formal. 38 test cover 5 luồng auth cả happy lẫn error path. Bật coverlet 5 phút là có số cụ thể.

**Q: Test có chạy parallel không?**
> NUnit mặc định serial trong cùng fixture. 38 test 2 giây nên chưa cần. Cần thì add `[Parallelizable(ParallelScope.All)]`.

**Q: Sao Demo 1 dùng `MockBehavior.Strict` mà Demo 2 lại Loose?**
> Demo 1 muốn ép service gọi GoTrue với data đã normalize → Strict. Demo 2 chỉ quan tâm output cuối (status + body) → Loose là đủ.

**Q: Sao Demo 1 ở service, Demo 2 ở controller?**
> Để show 2 kỹ thuật khác nhau. Service: mock infrastructure → test logic thuần. Controller: mock service + stub HttpContext → test HTTP contract. Hội đồng thấy team approach test có chiều sâu.

**Q: Có cần biết terminal mới setup được không?**
> Không. Visual Studio làm được toàn bộ qua GUI: tạo project NUnit qua Add New Project, cài package qua NuGet Package Manager, chạy test qua Test Explorer. Em có hướng dẫn click-by-click ở phụ lục B.

---

## Phụ lục B — Cài đặt NUnit qua Visual Studio GUI (không terminal)

> Cách này tương đương 3 lệnh terminal ở Slide 3, kết quả ra cùng một csproj.
> Thời gian thao tác: ~3 phút trong Visual Studio 2022.

### B1. Tạo Test Project (~60s)

1. Mở Visual Studio 2022, **File → Open → Project/Solution**, chọn `AI_Study_Hub_v2.sln`.
2. Trong **Solution Explorer**, chuột phải vào **Solution** (dòng trên cùng) → **Add → New Project...**
3. Cửa sổ "Add a new project" hiện ra. Trong ô search gõ `NUnit`.
4. Chọn template **NUnit Test Project** (icon C#, có chữ "Test"). Chú ý không chọn nhầm xUnit hay MSTest.
5. Click **Next**.
6. Cấu hình:
   - **Project name:** `AI_Study_Hub_v2.Tests`
   - **Location:** giữ nguyên (cùng thư mục solution)
   - **Solution:** Add to solution
7. Click **Next** → chọn **Framework: .NET 8.0** → **Create**.

*Visual Studio tự sinh `AI_Study_Hub_v2.Tests.csproj` với `NUnit`, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk` đã sẵn — tương đương lệnh `dotnet new nunit`.*

### B2. Reference sang main project (~30s)

1. Trong **Solution Explorer**, expand `AI_Study_Hub_v2.Tests`.
2. Chuột phải **Dependencies** → **Add Project Reference...**
3. Cửa sổ "Reference Manager" hiện ra. Trong tab **Projects → Solution**, tick checkbox `AI_Study_Hub_v2`.
4. Click **OK**.

*Tương đương `dotnet add reference ../AI_Study_Hub_v2/AI_Study_Hub_v2.csproj`. Sau bước này, test project import được mọi class trong main project.*

### B3. Cài 3 package phụ trợ (~90s)

1. Chuột phải `AI_Study_Hub_v2.Tests` → **Manage NuGet Packages...**
2. Tab **Browse**. Trong ô search gõ tên package → chọn từ danh sách → panel phải click **Install**.
3. Cài 3 package theo thứ tự:

| Package                                | Version    | Mục đích                                   |
| -------------------------------------- | ---------- | ------------------------------------------ |
| `FluentAssertions`                     | 6.12.x     | Assertion dạng `result.Should().Be(...)`   |
| `Moq`                                  | 4.20.x     | Mock object cho interface                  |
| `Microsoft.EntityFrameworkCore.InMemory` | 8.0.x   | DbContext chạy trong RAM (test isolation)  |

4. Mỗi lần Install, VS hiện dialog "Preview Changes" → click **Apply** → "License Acceptance" → **I Accept**.
5. Sau khi cài xong, kiểm tra **Solution Explorer → Dependencies → Packages** thấy đủ 3 package.

*Tương đương 3 lệnh `dotnet add package FluentAssertions`, `dotnet add package Moq`, `dotnet add package Microsoft.EntityFrameworkCore.InMemory`.*

### B4. (Optional) Cài NUnit.Analyzers (~30s)

Template **NUnit Test Project** mặc định **chưa** có `NUnit.Analyzers`. Đây là Roslyn analyzer báo warning ngay trong editor khi viết test sai cú pháp (ví dụ thiếu `[Test]`, dùng `Assert.AreEqual` sai thứ tự).

1. Manage NuGet Packages → search `NUnit.Analyzers` → Install version `4.x`.

*Khuyến nghị cài. Tương đương `dotnet add package NUnit.Analyzers`.*

### B5. Verify build + Test Explorer (~30s)

1. **Build → Build Solution** (`Ctrl+Shift+B`). Output panel hiện `Build succeeded. 0 Error(s)`.
2. **View → Test Explorer** (`Ctrl+E, T`). Test Explorer mở ra panel bên phải.
3. Test Explorer auto-discover test mặc định `UnitTest1.Test1` của template — click **Run All** thấy 1 test pass màu xanh.
4. Xóa file `UnitTest1.cs` của template, bắt đầu viết test thật.

### So sánh GUI vs Terminal

| Thao tác               | GUI (Visual Studio)                       | Terminal (`dotnet`)                      |
| ---------------------- | ----------------------------------------- | ---------------------------------------- |
| Tạo project            | Add New Project → NUnit Test Project      | `dotnet new nunit -n ... -f net8.0`      |
| Add vào solution       | Tự động khi Add New Project               | `dotnet sln add ...`                     |
| Reference main project | Reference Manager → tick checkbox         | `dotnet add reference ...`               |
| Cài package            | Manage NuGet Packages → Browse → Install  | `dotnet add package ...`                 |
| Build                  | `Ctrl+Shift+B`                            | `dotnet build`                           |
| Run test               | Test Explorer → Run All                   | `dotnet test`                            |
| **Tổng thời gian**     | **~3 phút (click + đợi UI)**              | **~30 giây (paste 5 lệnh)**              |

*GUI thân thiện cho người mới, terminal nhanh hơn cho người quen. Cả 2 ra cùng csproj cuối — không có khác biệt về kết quả.*

### Lưu ý khi demo GUI

- **NuGet load chậm lần đầu:** lần đầu mở Manage NuGet Packages, VS phải tải metadata, có thể đợi 5-10s. Pre-load trước buổi để tránh khán giả thấy spinner.
- **Visual Studio Code không có template NUnit Test Project sẵn:** chỉ có Visual Studio 2022 (Community/Pro). VS Code phải dùng terminal.
- **Tên project phải khớp namespace:** `AI_Study_Hub_v2.Tests` để đồng bộ với code đã viết. Đặt sai phải refactor namespace.
- **Mặc định template tạo file `UnitTest1.cs`:** xóa file này trước khi viết test thật để tránh nhầm lẫn.

---

## Bảng thời gian tham chiếu

| Slide | Nội dung                            | ⏱     | Checkpoint |
| ----- | ----------------------------------- | ----- | ---------- |
| 1     | Mở đầu                              | 25s   | 0:25       |
| 2     | Tại sao NUnit                       | 25s   | 0:50       |
| 3     | Cài đặt csproj                      | 45s   | 1:35       |
| 4     | Demo 1 — Setup + input              | 60s   | 2:35       |
| 5     | Demo 1 — GWT + 3 nhóm assert        | 60s   | 3:35       |
| 6     | Demo 2 — Setup + input              | 50s   | 4:25       |
| 7     | Demo 2 — GWT + 3 tầng verify        | 50s   | 5:15       |
| **8** | **Break-then-fix (live thực hành)** | **120s** | **7:15** |
| 9     | Run All 38 test                     | 30s   | 7:45       |
| 10    | Closing                             | 15s   | 8:00       |
|       | **+ Buffer chuyển slide / IDE**     | ~1:30 | **9:30**   |
|       | **+ Q&A đệm 1 câu**                 | ~0:30 | **10:00**  |

---

## Pre-flight checklist (làm trước buổi 30 phút)

- [ ] `dotnet test` chạy pass 38/38 trong terminal — verify lần cuối
- [ ] Mở project trong VS, build 1 lần để warmup (lần đầu `dotnet test` luôn lâu)
- [ ] Pin Test Explorer ở dock dưới, filter sẵn `RegisterAsync_HappyPath`
- [ ] Split editor 2 file: `SupabaseAuthService.cs` (line 44) + `SupabaseAuthServiceTests.cs` (line 47)
- [ ] Zoom editor 16-18pt, theme Light
- [ ] Test thử kịch bản break-then-fix **2 lần** ở nhà — đo thời gian phải ≤120s
- [ ] Tắt Discord / Telegram / mail / Focus assist
- [ ] Copy `demo_backup/` (toàn bộ project) phòng `Ctrl+Z` không undo được
- [ ] Screenshot kết quả 38 pass — fallback nếu IDE crash giữa demo
- [ ] Sạc full + cắm sạc khi demo
- [ ] Resolution 1920×1080 (không 4K) — chữ to cho khán giả xa

---

**END.** Đọc trôi chảy, không improvise. Đoạn `*nghiêng*` là hint, không đọc thành tiếng.

**Quy tắc cắt giảm khẩn cấp** (theo thứ tự ưu tiên nếu tới Slide 7 mà đã vượt 5:30):
1. Slide 9 (Run All) — bỏ phần `dotnet test` ở terminal, chỉ giữ Test Explorer (tiết kiệm 15s)
2. Slide 5 / Slide 7 — gộp 3 nhóm assert / 3 tầng verify thành 2, bỏ nhóm "mock interaction" / "result type" (tiết kiệm 20s mỗi slide)
3. Slide 8 — nếu test fail không hiện log rõ → bỏ phần `Ctrl+Z` cuối, để màn hình ở trạng thái fail và nói "em revert sau buổi", chuyển slide tiếp (tiết kiệm 30s)
4. **Tuyệt đối không bỏ Slide 8** — đây là slide ấn tượng nhất, cắt slide khác trước.
