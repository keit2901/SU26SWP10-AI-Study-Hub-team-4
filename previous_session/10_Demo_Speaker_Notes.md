# 10 — Speaker Notes — Demo NUnit (script đọc nguyên văn)

> **Mục đích:** đoạn nói chính xác cho buổi demo NUnit. Đọc nguyên văn được, không cần improvise. Cap slide theo `09_NUnit_Demo_Script.md` Section 7 (cheat sheet path + line).
> **Demo case:** Service layer (`RegisterAsync_HappyPath`) + Controller layer (`Login_WhenServiceThrowsAuthException`).
> **Tổng thời lượng:** ~12 phút (intro 1 phút + Demo 1 ~5 phút + Demo 2 ~4 phút + closing 2 phút).
> **Author:** OpenCode (kr/claude-opus-4.7) — 2026-05-24

---

## 0. Cách dùng file này

- Mỗi slide có 3 phần: **[Cap màn hình]** (chỉ dẫn cap), **[Hiện slide]** (gạch đầu dòng cho slide), **[Speaker note]** (đoạn nói nguyên văn).
- Phần `[Speaker note]` viết theo lối nói tự nhiên, không phải văn viết. Đọc thành tiếng nghe ổn.
- Đoạn in `*nghiêng*` là **hint cho người trình bày** (không đọc thành tiếng).
- Mỗi slide có dấu `⏱` chỉ thời gian gợi ý.

---

## Slide 1 — Mở đầu (⏱ 30s)

### [Hiện slide]
- **Demo: Unit Test với NUnit cho AI_Study_Hub_v2**
- Stack: ASP.NET Core 8 + Blazor Server + Supabase Local + EF Core
- 38/38 unit test pass — chạy offline, không cần Docker

### [Speaker note]
> Em chào thầy/cô và các bạn. Phần demo này em sẽ trình bày cách team mình triển khai unit test cho project AI_Study_Hub_v2 bằng NUnit. Project là Blazor Server kết hợp Web API, chạy trên ASP.NET Core 8, dùng Supabase Local cho authentication và Postgres + pgvector cho data layer.
>
> Hiện tại bộ test có 38 test case, tất cả đều pass. Điểm em muốn nhấn mạnh ngay từ đầu: **toàn bộ test này chạy được offline** — kể cả khi Docker chưa start, kể cả khi không có mạng, kể cả khi Postgres chưa cài. Đây là cách team mình thiết kế để test có thể chạy trên bất kỳ máy CI/CD nào mà không cần dựng infrastructure.
>
> Demo của em sẽ đi qua 3 phần: thứ nhất là cách cài đặt NUnit từ con số 0, thứ hai là demo một test ở tầng service, thứ ba là demo một test ở tầng controller. Sau đó em sẽ chạy live cho thầy/cô thấy 38 test pass.

---

## Slide 2 — Tại sao chọn NUnit (⏱ 45s)

### [Hiện slide]
- 3 framework phổ biến: **NUnit / xUnit / MSTest**
- Team chọn **NUnit 3.14** vì:
  - Cú pháp gần JUnit (đa số đã học Java)
  - Setup/Teardown rõ ràng qua attribute
  - Tài liệu Việt nhiều hơn

### [Speaker note]
> Trong .NET có 3 framework test phổ biến: NUnit, xUnit và MSTest. Team em chọn NUnit phiên bản 3.14, lý do chính có 3 cái.
>
> Thứ nhất, cú pháp NUnit gần với JUnit của Java — mà Java thì cả lớp đã học từ năm 2 rồi, nên ai cũng quen `[Test]`, `[SetUp]`, `[TearDown]`. xUnit thì khác hơn, dùng constructor và `IDisposable` thay cho SetUp/TearDown, đọc khó hơn.
>
> Thứ hai, NUnit có Setup và Teardown rõ ràng qua attribute — tách biệt phần chuẩn bị data với phần test thật, code test sạch hơn.
>
> Thứ ba, tài liệu tiếng Việt và ví dụ trong các project FPT đều dùng NUnit, nên team mình tham khảo dễ hơn.
>
> Còn về sức mạnh thì cả 3 framework gần như ngang nhau, đây chỉ là vấn đề preference.

---

## Slide 3 — Hướng dẫn cài đặt (⏱ 60s)

### [Cap màn hình]
- Mở `AI_Study_Hub_v2.Tests.csproj` (line 1-33) — show full file
- Line numbers ON, zoom ~14pt, theme Light cho slide rõ

### [Hiện slide]
- 3 bước cài đặt:
  1. `dotnet new nunit -n AI_Study_Hub_v2.Tests -f net8.0`
  2. `dotnet sln add` + `dotnet add reference` — link sang main project
  3. Add packages: FluentAssertions, Moq, EFCore.InMemory
- 9 PackageReference + GlobalUsings

### [Speaker note]
> Slide này là csproj của test project. Em muốn highlight 4 chỗ.
>
> Thứ nhất, ba package lõi của NUnit ở dòng 19 đến 21: `NUnit` là framework chính, `NUnit3TestAdapter` là cầu nối giữa `dotnet test` và NUnit — không có adapter này thì runner không discover được test. `NUnit.Analyzers` là Roslyn analyzer, sẽ hiện warning ngay trong VS khi viết test sai cú pháp.
>
> Thứ hai, mấy package phụ trợ ở dòng 13 đến 18: `FluentAssertions` để viết assertion đọc như tiếng Anh — `result.Should().Be(2)` thay vì `Assert.AreEqual(2, result)`. `Moq` để tạo mock cho interface — không gọi service thật. `EFCore.InMemory` để DbContext chạy trong RAM — không cần Postgres thật.
>
> Thứ ba, dòng 25, `ProjectReference` link từ test project sang main project. Cái này quan trọng — nếu sai sẽ không reference được class trong main project, build fail luôn.
>
> Thứ tư, dòng 29 và 30, `Using Include`. Đây là tính năng Global Usings của C# 10, giúp khỏi phải viết `using NUnit.Framework;` ở đầu mỗi file test. Tất cả file test trong project tự động có sẵn 2 namespace này, code sạch hơn nhiều.
>
> Tóm lại: chỉ cần 3 lệnh `dotnet new nunit`, `dotnet add reference` và `dotnet add package` là xong setup. Toàn bộ trong vòng 1 phút.

---

## Slide 4 — Demo 1: Test case là gì? (⏱ 30s)

### [Hiện slide]
- **`RegisterAsync_HappyPath`** — Service layer
- Verify luồng đăng ký user thành công:
  - Tạo identity ở Supabase GoTrue
  - Mirror profile vào `public.users`
  - Trả access token + role Student
- **Không đụng Postgres thật, không đụng GoTrue thật** — mọi dependency bị mock

### [Speaker note]
> Test đầu tiên em demo là `RegisterAsync_HappyPath`, nằm ở tầng Service. Nó verify luồng đăng ký user thành công — gồm 3 việc: tạo identity bên Supabase GoTrue, mirror profile vào bảng `public.users` của mình, và trả về access token kèm thông tin user role Student.
>
> Điểm quan trọng: test này **không đụng Postgres thật, không đụng GoTrue thật**. Mọi dependency bên ngoài đều bị mock hết. Nó là unit test thuần — chỉ test logic bên trong method, không test integration.

---

## Slide 5 — Demo 1: Dữ liệu test (⏱ 45s)

### [Cap màn hình]
- File `Services/SupabaseAuthServiceTests.cs` line 46-76 (phần Arrange + Act)

### [Hiện slide]
- **Input cố tình "bẩn":**
  - `Email = "  ALICE@aistudyhub.local  "` (whitespace + UPPERCASE)
  - `FullName = "  Alice A.  "` (whitespace 2 đầu)
  - `Username = "alice"`, `Password = "Password!1"`
- **Mock GoTrue trả:** AccessToken `"access.jwt.token"`, RefreshToken `"rt-abc"`, User.Id random Guid
- **DB pre-seed:** 2 role (Admin id=1, Student id=2), bảng users rỗng

### [Speaker note]
> Slide này show input của test. Em cố tình thiết kế dữ liệu **bẩn** để verify một thứ rất quan trọng — service phải tự normalize trước khi gọi external service.
>
> Nhìn vào email, em truyền `"  ALICE@aistudyhub.local  "` — có khoảng trắng đầu và cuối, lại còn viết HOA. Field FullName cũng có khoảng trắng. Đây là dữ liệu giả lập user gõ tay vào form đăng ký, có thể có khoảng trắng do Ctrl+V, có thể caps lock.
>
> Phần mock IGoTrueClient, em setup nó **chỉ trả về session** khi nhận được email đã trim và lowercase, FullName đã trim. Nếu service gọi GoTrue với email raw có khoảng trắng → mock không match → test fail. Đây là cách dùng mock để **ép service phải xử lý đúng**.
>
> DB thì pre-seed sẵn 2 role Admin với Student, để khi service tìm role Student cho user mới, nó tìm được. Helper `TestDb.CreateInMemory()` em sẽ giải thích ở slide sau.

---

## Slide 6 — Demo 1: Scenario (Given-When-Then) (⏱ 60s)

### [Cap màn hình]
- File `Services/SupabaseAuthServiceTests.cs` line 77-98 (phần Assert — 3 nhóm)
- Highlight 3 block comment `// ── ASSERT ...`

### [Hiện slide]
- **Given** — DB in-memory đã seed 2 role; mock GoTrue ready trả session
- **When** — gọi `RegisterAsync` với input bẩn
- **Then — verify 3 nhóm:**
  1. Response trả client đúng (token + email normalized + role Student)
  2. DB persist đúng (1 row, link `SupabaseUserId`, role Student)
  3. Service gọi GoTrue với data đã normalize (mock interaction)

### [Speaker note]
> Bây giờ em đi qua scenario theo cú pháp Given-When-Then.
>
> **Given** — DB in-memory đã được seed sẵn 2 role Admin và Student. Mock GoTrue đã setup sẵn để trả về session khi service gọi đúng cách.
>
> **When** — em gọi `RegisterAsync` với cái input bẩn vừa show ở slide trước.
>
> **Then** — em verify 3 nhóm assert. Cấu trúc 3 nhóm này theo đúng pattern AAA — **A**rrange, **A**ct, **A**ssert — và phần Assert chia 3 thứ.
>
> Nhóm thứ nhất, **response trả ra client**: access token, refresh token đúng giá trị mock setup; email và FullName phải đã được normalize — `"alice@aistudyhub.local"` không còn khoảng trắng, không còn HOA; role là Student vì user mới đăng ký mặc định là Student; isActive là true.
>
> Nhóm thứ hai, **side effect ở DB**: phải có đúng 1 row trong `public.users` được persist; link đúng `SupabaseUserId` về với identity bên GoTrue; role gắn đúng là Student; `TotalTokensUsed` khởi tạo bằng 0.
>
> Nhóm thứ ba, cũng là điểm em muốn nhấn mạnh nhất — **mock interaction**. Em dùng `gotrue.VerifyAll()`. Vì lúc setup em dùng `MockBehavior.Strict`, nếu service gọi mock với args **khác** setup thì sẽ throw exception. Mock setup yêu cầu email phải là `"alice@aistudyhub.local"` đã normalize, FullName là `"Alice A."` đã trim. Nếu service quên trim → test fail. Đây là cách dùng mock để **ép quy tắc business**, không chỉ check output.
>
> Một câu chốt cho slide này: test này KHÔNG cần Docker, KHÔNG cần Postgres, KHÔNG cần network. Chạy trong vòng 30 mili giây.

---

## Slide 7 — Demo 1: Helper TestDb (optional, ⏱ 30s)

### [Cap màn hình]
- File `Support/TestDb.cs` line 13-46

### [Hiện slide]
- `databaseName ?? Guid.NewGuid().ToString()` → mỗi test 1 DB riêng
- Pre-seed 2 role Admin + Student
- `using var db = TestDb.CreateInMemory()` → auto dispose

### [Speaker note]
> Slide phụ: helper `TestDb.CreateInMemory()`. Đây là factory method tạo DbContext in-memory mới cho mỗi test.
>
> Điểm quan trọng nhất ở dòng `databaseName ?? Guid.NewGuid().ToString()` — nếu không truyền tên DB, nó tự sinh Guid random làm tên. Nghĩa là **mỗi test có 1 DB riêng biệt hoàn toàn**, không bao giờ đụng dữ liệu của test khác. Test isolation tuyệt đối, có thể chạy parallel mà không sợ race condition.
>
> Helper này pre-seed sẵn 2 role Admin và Student vì hầu hết test đều cần. Test nào không cần thì truyền `seedRoles: false`.

*Nếu thiếu giờ → skip slide này, ghép vào lời giảng slide 6.*

---

## Slide 8 — Demo 2: Test case là gì? (⏱ 30s)

### [Hiện slide]
- **`Login_WhenServiceThrowsAuthException`** — Controller layer
- Verify khi service ném `AuthException` → controller map đúng:
  - Status code đúng (401)
  - Body shape `ApiErrorResponse { code, message }`
- **Không đụng business logic, không đụng DB** — chỉ test HTTP contract

### [Speaker note]
> Test thứ hai em demo nằm ở tầng cao hơn — tầng Controller. Tên test là `Login_WhenServiceThrowsAuthException`.
>
> Tình huống: user gõ sai password, service tầng dưới ném `AuthException` với status code 401 và error code `"invalid_credentials"`. Câu hỏi đặt ra: **controller xử lý exception đó như thế nào?** Có map đúng thành response 401 không? Body trả về có đúng format `ApiErrorResponse` cho client tự handle không?
>
> Nếu controller quên `try/catch`, exception sẽ bubble lên ASP.NET runtime → trả 500 InternalServerError. Lúc đó user gõ sai pass mà nhận message "lỗi server" — sai hoàn toàn.
>
> Test này **không đụng business logic** — service đã bị mock; **không đụng DB** — không cần. Nó chỉ test cái em gọi là **HTTP contract** — controller phải dịch exception ra response đúng quy ước.

---

## Slide 9 — Demo 2: Dữ liệu test (⏱ 30s)

### [Cap màn hình]
- File `Controllers/AuthControllerTests.cs` line 114-130

### [Hiện slide]
- **Input request:** `Email = "a@x.com"`, `Password = "p"`
- **Mock IAuthService ném:**
  - `AuthException(401, "invalid_credentials", "Email or password is incorrect.")`
- **HttpContext giả lập:** không user, không Bearer header (Login là `[AllowAnonymous]`)

### [Speaker note]
> Input đơn giản — email và password bất kỳ vì test này không quan tâm validation, chỉ quan tâm cách controller phản ứng khi service throw.
>
> Phần mock IAuthService, em setup nó **ném exception** thay vì trả session. `ThrowsAsync(new AuthException(...))` — đây là cách Moq giả lập "service xử lý xong rồi quyết định ném exception vì pwd sai". Tham số gồm 3 thứ: status code 401, error code `"invalid_credentials"`, và message human-readable.
>
> HttpContext em build qua helper `BuildSut` — không cần user vì endpoint Login cho phép anonymous, không cần Bearer header vì Login chưa có token.

---

## Slide 10 — Demo 2: Scenario (Given-When-Then) (⏱ 60s)

### [Cap màn hình]
- File `Controllers/AuthControllerTests.cs` line 23-45 (helper BuildSut) — optional
- Hoặc giữ slide line 114-130 từ slide 9

### [Hiện slide]
- **Given** — controller build với mock service sẽ throw 401; HttpContext anonymous
- **When** — gọi `sut.Login(...)` → service throw → controller catch + map
- **Then — verify 3 thứ:**
  1. Action result type = `ObjectResult` (không phải Ok, không bubble)
  2. Status code = `401`
  3. Body = `ApiErrorResponse { Code = "invalid_credentials", Message = "Email or password is incorrect." }`

### [Speaker note]
> Cấu trúc Given-When-Then cho test này.
>
> **Given** — controller được build với mock service đã setup sẽ ném `AuthException(401)`. HttpContext rỗng vì Login là endpoint công khai.
>
> **When** — em gọi method `Login` của controller. Bên trong, nó sẽ gọi `IAuthService.LoginAsync` — và mock của em ném exception. Câu hỏi: controller xử lý ra sao?
>
> **Then** — em verify 3 tầng.
>
> Tầng đầu tiên: **action result type**. Em assert kết quả phải là `ObjectResult`. Không phải `OkObjectResult` vì không phải success. Cũng không được phép để exception bubble lên runtime — runtime sẽ trả 500 thay vì 401.
>
> Tầng thứ hai: **status code phải đúng 401**. Status code này được lấy nguyên từ `AuthException.StatusCode`. Nghĩa là service quyết status code, controller chỉ pass-through.
>
> Tầng thứ ba: **body shape**. Phải là kiểu `ApiErrorResponse` — DTO chuẩn cho mọi lỗi của API mình. Trong đó `Code = "invalid_credentials"` để client đọc code mà xử lý logic — ví dụ "code này thì hiện cảnh báo đỏ", không cần parse message bằng tiếng Anh. Còn `Message` thì hiển thị cho user thấy. Cả 2 lấy nguyên từ `AuthException`.
>
> Câu chốt: test này verify **hợp đồng HTTP** giữa server và client. Service muốn báo 401 thì client phải nhận đúng 401, đúng error code. Sau này nếu có ai sửa controller, vô tình swallow exception hay đổi format body → test này sẽ fail ngay, bắt được lỗi trước khi merge.

---

## Slide 11 — Live run (⏱ 45s)

### [Thao tác]
1. Mở Test Explorer (`Ctrl+E, T` hoặc `View → Test Explorer`)
2. Click **"Run All"** — đợi ~3s
3. Cap màn hình 38 test all green
4. Mở Terminal (`Ctrl+\``), paste:
   ```powershell
   dotnet test AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/AI_Study_Hub_v2.Tests.csproj --nologo
   ```
5. Cap dòng `Passed!  - Failed: 0, Passed: 38, ...`

### [Hiện slide]
- 38/38 test pass
- Duration ~2 giây
- Chạy offline — không cần Docker / Postgres / network

### [Speaker note]
> Cuối cùng em chạy live cho thầy/cô thấy. Em mở Test Explorer của Visual Studio, click Run All. Trong vòng khoảng 2 giây — 38 test, tất cả icon xanh.
>
> *Đợi run xong, chỉ vào màn hình.*
>
> Đây là 38 test — chia 3 fixture: 3 sanity test, 18 test cho service layer cover đầy đủ 5 luồng auth gồm Register, Login, Refresh, Logout, Me — cả happy path lẫn error path; và 17 test cho controller layer cover claim parsing, exception mapping, Bearer header parsing.
>
> Em chạy lại bằng terminal cho thầy/cô thấy `dotnet test` cũng cho kết quả y hệt. Đây là moment "show, don't tell" — test này chạy trên CI/CD GitHub Actions, GitLab cũng ra y hệt vì không cần infrastructure.

---

## Slide 12 — Closing (⏱ 30s)

### [Hiện slide]
- 38 test, 100% pass
- Cover: Service layer (business logic) + Controller layer (HTTP contract)
- Offline-first — chạy được trên mọi CI/CD
- Tools: NUnit + FluentAssertions + Moq + EF Core InMemory

### [Speaker note]
> Tổng kết phần demo. Hiện tại bộ test có 38 case, 100 phần trăm pass. Cover được cả 2 tầng quan trọng nhất của ứng dụng — tầng business logic ở service, và tầng HTTP contract ở controller. Test offline-first, chạy được trên bất kỳ máy CI/CD nào mà không cần dựng Docker hay Postgres.
>
> Tools chính team mình dùng: NUnit làm framework, FluentAssertions cho assertion đọc tự nhiên, Moq cho mock object, EF Core InMemory để giả lập DbContext.
>
> Phần Phase 2 sắp tới — Document Management và RAG — bộ test sẽ mở rộng thêm coverage cho các service mới như chunking, embedding, vector search. Em xin hết phần demo. Cảm ơn thầy/cô và các bạn.

---

## Phụ lục — Q&A đáp án ngắn (đọc khi hội đồng hỏi)

**Q: Tại sao chọn NUnit thay vì xUnit?**
> Cú pháp NUnit gần JUnit, đa số đã học Java. Setup/Teardown qua attribute rõ hơn pattern Constructor/Dispose của xUnit. Tài liệu Việt nhiều hơn. Sức mạnh thì cả 2 ngang nhau.

**Q: Sao không test Postgres thật?**
> Đây là **unit test**, mục tiêu isolate business logic. Test Postgres thật là **integration test** — phase sau, đã quyết skip cho phase 1 do ROI thấp so với effort.

**Q: Mock vs Stub vs Fake khác nhau gì?**
> Stub trả giá trị hard-code, không verify call. Mock có verify call (số lần, args). Fake là implementation thật nhưng đơn giản. Demo của em dùng cả 3 — mock cho IGoTrueClient, stub cho IAuthenticationService, fake là DbContext InMemory.

**Q: Coverage bao nhiêu phần trăm?**
> Phase 1 chưa đo formal. 38 test cover 5 luồng auth cả happy lẫn error path. Em có thể bật coverlet collect coverage trong vòng 5 phút nếu thầy/cô cần con số cụ thể.

**Q: Test có chạy parallel không?**
> NUnit mặc định serial trong cùng fixture. 38 test chạy 2 giây nên team chưa cần parallel. Nếu cần thì add `[Parallelizable(ParallelScope.All)]`.

**Q: Sao Demo 1 dùng `MockBehavior.Strict` mà Demo 2 lại Loose?**
> Demo 1 muốn verify service gọi GoTrue với data đã normalize, nên Strict — gọi sai args là fail. Demo 2 chỉ quan tâm output cuối cùng (status + body), mock service gọi đúng method là đủ, không cần Strict.

**Q: Tại sao Demo 1 ở tầng service, Demo 2 ở tầng controller?**
> Để show 2 kỹ thuật khác nhau. Service: mock infrastructure (HTTP client, DB) → test logic thuần. Controller: mock service + stub HttpContext → test HTTP contract. Hội đồng thấy được team approach test có chiều sâu.

---

**END.** Đọc trôi chảy, không cần improvise. Đoạn `*nghiêng*` trong file là hint, không đọc thành tiếng. Khi nào team đổi demo case, update Slide 4-10 + Phụ lục Q&A.
