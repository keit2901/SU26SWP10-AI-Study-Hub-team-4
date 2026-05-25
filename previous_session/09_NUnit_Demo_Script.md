# 09 — NUnit Demo Script (cài đặt + 2 demo walkthrough)

> **Mục đích:** kịch bản trình bày NUnit cho team / hội đồng SWP391. Dùng đúng code đang có trong `AI_Study_Hub_v2.Tests` (38/38 pass). Đọc xong file này, người demo có thể: (1) trả lời "tại sao chọn NUnit", (2) dựng test project từ zero, (3) walkthrough 2 test case có sẵn theo AAA pattern, (4) trả lời các câu hội đồng hay hỏi.
> **Author:** OpenCode (kr/claude-opus-4.7)
> **Ngày:** 2026-05-24
> **Audience:** team Team 4 + hội đồng. Giả định người xem **chưa từng dùng NUnit** nhưng đã biết C# cơ bản.

---

## 0. Flow demo (timing gợi ý ~15 phút)

| Phút | Section | Nội dung |
|---|---|---|
| 0-1 | §1 | Tại sao NUnit, so với xUnit / MSTest |
| 1-5 | §2 | Hướng dẫn cài đặt (from zero) |
| 5-10 | §3 | **Demo 1 — Service layer** — `RegisterAsync_HappyPath` (mock IGoTrueClient + EF InMemory) |
| 10-14 | §4 | **Demo 2 — Controller layer** — `Login_WhenServiceThrowsAuthException` (mock IAuthService + stub HttpContext, map exception → ApiErrorResponse) |
| 14-15 | §5 | Run `dotnet test` live, show 38/38 pass |
| dự phòng | §6 | Các câu hội đồng hay hỏi + đáp án |

---

## 1. NUnit là gì + tại sao chọn

**NUnit** là framework unit test cho .NET, port từ JUnit (Java). Test được đánh dấu bằng attribute `[Test]`, group bằng `[TestFixture]`, runner discover qua `Microsoft.NET.Test.Sdk` + adapter `NUnit3TestAdapter`.

**3 framework phổ biến cho .NET:** xUnit, NUnit, MSTest. Team chọn **NUnit 3.14** vì:

| Tiêu chí | NUnit | xUnit | MSTest |
|---|---|---|---|
| Cú pháp gần JUnit (đa số thành viên đã học Java) | ✅ | ❌ (constructor/dispose pattern) | ✅ |
| Setup/Teardown rõ ràng (`[SetUp]`, `[TearDown]`) | ✅ | ❌ (phải dùng IDisposable) | ✅ |
| Fluent assertions tích hợp tốt | ✅ | ✅ | ⚠️ |
| Tài liệu Việt + ví dụ FPT | ✅ nhiều | ⚠️ ít hơn | ✅ |
| Visual Studio Test Explorer support | ✅ | ✅ | ✅ |

**Quyết định:** NUnit 3.14 + FluentAssertions 6.12 + Moq 4.20 + EF Core InMemory 8.0.

---

## 2. Hướng dẫn cài đặt (from zero)

> Giả lập: máy mới clone repo, chưa có test project. Đi từng bước cho audience theo dõi được.

### 2.1 Tạo test project

Từ root solution (`D:\FPT\summer2026\SWP391\AI_Study_Hub_v2`), chạy:

```powershell
# Tạo project NUnit dạng net8.0
dotnet new nunit -n AI_Study_Hub_v2.Tests -f net8.0

# Add vào solution
dotnet sln AI_Study_Hub_v2.sln add AI_Study_Hub_v2.Tests/AI_Study_Hub_v2.Tests.csproj

# Reference từ test project sang main project
dotnet add AI_Study_Hub_v2.Tests/AI_Study_Hub_v2.Tests.csproj reference AI_Study_Hub_v2.csproj
```

`dotnet new nunit` đã tự install:
- `Microsoft.NET.Test.Sdk` — test platform host
- `NUnit` — framework
- `NUnit3TestAdapter` — runner adapter cho `dotnet test` / VS Test Explorer
- `NUnit.Analyzers` — Roslyn analyzers, warning khi test viết sai

### 2.2 Add packages bổ sung

```powershell
cd AI_Study_Hub_v2.Tests

# Fluent assertions — readability
dotnet add package FluentAssertions --version 6.12.1

# Mocking
dotnet add package Moq --version 4.20.72

# EF Core InMemory — test DAL không cần Postgres
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 8.0.10

# (Tuỳ chọn) Mvc.Testing cho integration test sau này
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 8.0.10

# (Tuỳ chọn) Coverage
dotnet add package coverlet.collector --version 6.0.0
```

**Giải thích từng package — hỏi-đáp với hội đồng:**

| Package | Vai trò | Tại sao cần |
|---|---|---|
| `Microsoft.NET.Test.Sdk` | Test host process | Bắt buộc để `dotnet test` chạy |
| `NUnit` | Framework + attribute (`[Test]`, `[TestFixture]`) | Lõi |
| `NUnit3TestAdapter` | Adapter giữa Test SDK và NUnit | Không có nó test sẽ không discover |
| `NUnit.Analyzers` | Lint test code | Bắt sai cú pháp sớm (vd: `[Test]` trên method có param mà không có `[TestCase]`) |
| `FluentAssertions` | `result.Should().Be(...)` | Đọc test như đọc tiếng Anh, error message rõ hơn |
| `Moq` | Tạo mock cho interface | Không gọi GoTrue thật, không gọi DB Postgres thật |
| `EFCore.InMemory` | DbContext chạy trong RAM | Test DAL nhanh, isolated |
| `coverlet.collector` | Đo code coverage | Optional, dùng khi CI/CD báo coverage % |

### 2.3 Cấu hình `.csproj` (tham khảo bản đang dùng)

Mở `AI_Study_Hub_v2.Tests/AI_Study_Hub_v2.Tests.csproj`. Bản project đang dùng:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.10" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.10" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit.Analyzers" Version="3.9.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AI_Study_Hub_v2.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="NUnit.Framework" />
    <Using Include="FluentAssertions" />
  </ItemGroup>

</Project>
```

**2 chỗ đáng chú ý cho hội đồng:**

1. `<IsTestProject>true</IsTestProject>` — đánh dấu cho `dotnet test` discover, nếu thiếu thì `dotnet test` sẽ chạy nhưng không tìm thấy test.
2. `<Using Include="NUnit.Framework" />` + `<Using Include="FluentAssertions" />` — dùng tính năng **Global Usings** của C# 10. Khỏi phải `using NUnit.Framework;` ở mỗi file test. Sạch hơn.

### 2.4 Verify cài đặt

Tạo file `AI_Study_Hub_v2.Tests/Probe.cs`:

```csharp
namespace AI_Study_Hub_v2.Tests;

[TestFixture]
public class Probe
{
    [Test]
    public void Sanity() => (1 + 1).Should().Be(2);
}
```

Run:
```powershell
dotnet test AI_Study_Hub_v2.Tests/AI_Study_Hub_v2.Tests.csproj
```

Expected output: `Passed!  - Failed: 0, Passed: 1, Skipped: 0`. Nếu thấy dòng đó → **stack OK**, xoá file probe và đi tiếp.

---

## 3. Demo 1 — Service layer: `RegisterAsync_HappyPath` (mock IGoTrueClient + EF InMemory)

**File:** `AI_Study_Hub_v2.Tests/Services/SupabaseAuthServiceTests.cs:46-98` (test method).
**Helper:** `AI_Study_Hub_v2.Tests/Support/TestDb.cs` (in-memory DbContext factory).

### 3.1 Bối cảnh

`SupabaseAuthService.RegisterAsync` là method core cho luồng đăng ký user:
1. Validate username chưa bị trùng trong `public.users`
2. Tìm Role `Student` trong DB
3. Gọi `IGoTrueClient.SignUpAsync(...)` → tạo identity bên GoTrue
4. Insert profile mirror vào `public.users` (cùng `supabase_user_id`)
5. Return `AuthResponse` chứa access/refresh token + UserDto

**Tại sao test method này?**
- Có dependency cần mock (`IGoTrueClient` — gọi HTTP đến GoTrue thật → KHÔNG được phép trong unit test)
- Có dependency cần in-memory thay (`AppDbContext` — đụng Postgres thật → KHÔNG được phép trong unit test)
- Cover cả side effect (DB persist) lẫn return value
- Demo đẹp AAA pattern + 3 nhóm assert

### 3.2 Test case — bảng đặc tả

| Field | Value |
|---|---|
| ID | SVC-Reg-01 |
| Test | `RegisterAsync_HappyPath_CreatesGoTrueIdentity_AndMirrorsProfile_AsStudent` |
| Layer | Service (business logic) |
| SUT | `SupabaseAuthService.RegisterAsync` |
| Mock | `IGoTrueClient` (Strict) |
| Fake | `AppDbContext` qua EF Core InMemory provider |
| Pre-conditions | DB clean, có sẵn 2 role Admin + Student (do `TestDb.CreateInMemory()` seed) |

### 3.3 Scenario

**Given:**
- DB in-memory mới, đã seed `roles { Admin(id=1), Student(id=2) }`
- Mock `IGoTrueClient.SignUpAsync` được setup trả `GoTrueSession` có `User.Id = supabaseUserId` random, `Email = "alice@aistudyhub.local"`, `AccessToken = "access.jwt.token"`, `RefreshToken = "rt-abc"`

**When:**
- Gọi `sut.RegisterAsync(request, userAgent: "nunit", ipAddress: "127.0.0.1")` với:
  - `Email = "  ALICE@aistudyhub.local  "` (cố tình có whitespace + uppercase để test trim + lowercase)
  - `Username = "alice"`
  - `FullName = "  Alice A.  "`
  - `Password = "Password!1"`

**Then (expected):**

*Response shape:*
- `result.AccessToken == "access.jwt.token"`
- `result.RefreshToken == "rt-abc"`
- `result.TokenType == "Bearer"`
- `result.ExpiresIn == 3600`
- `result.User.Email == "alice@aistudyhub.local"` (đã trim + lowercase)
- `result.User.Username == "alice"`
- `result.User.FullName == "Alice A."` (đã trim)
- `result.User.Role == "Student"`
- `result.User.IsActive == true`

*Side effect — profile mirror trong `public.users`:*
- Đúng 1 row được persist
- `SupabaseUserId == supabaseUserId` (link tới `auth.users`)
- `Username == "alice"`, `FullName == "Alice A."`
- `Role.RoleName == "Student"`
- `TotalTokensUsed == 0`
- `IsActive == true`

*Mock interaction:*
- `gotrue.SignUpAsync(...)` được gọi ĐÚNG 1 lần với metadata `{ "username": "alice", "full_name": "Alice A." }`

### 3.4 Code thật (paste từ project)

**Helper `TestDb`:**

```csharp
internal static class TestDb
{
    public static AppDbContext CreateInMemory(string? databaseName = null, bool seedRoles = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var ctx = new AppDbContext(options);

        if (seedRoles)
        {
            ctx.Roles.AddRange(
                new Role { Id = 1, RoleName = Role.AdminRoleName, Description = "Admin",   CreatedAt = ... },
                new Role { Id = 2, RoleName = Role.StudentRoleName, Description = "Student", CreatedAt = ... });
            ctx.SaveChanges();
        }

        return ctx;
    }
}
```

Source: `AI_Study_Hub_v2.Tests/Support/TestDb.cs:13-46`

> **Highlight cho hội đồng:** `databaseName ?? Guid.NewGuid().ToString()` — mỗi test 1 DB riêng. Test isolation tuyệt đối, chạy parallel không sợ race condition.

**Test case (full):**

```csharp
[Test]
public async Task RegisterAsync_HappyPath_CreatesGoTrueIdentity_AndMirrorsProfile_AsStudent()
{
    // ── ARRANGE ──────────────────────────────────────────────────────
    using var db = TestDb.CreateInMemory();
    var supabaseUserId = Guid.NewGuid();
    var session = BuildSession(supabaseUserId, "alice@aistudyhub.local");

    var gotrue = new Mock<IGoTrueClient>(MockBehavior.Strict);
    gotrue
        .Setup(g => g.SignUpAsync(
            "alice@aistudyhub.local",
            "Password!1",
            It.Is<Dictionary<string, object?>>(m =>
                (string)m["username"]! == "alice"
                && (string)m["full_name"]! == "Alice A."),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(session);

    var sut = BuildSut(db, gotrue.Object);

    // ── ACT ──────────────────────────────────────────────────────────
    var result = await sut.RegisterAsync(
        new RegisterRequest
        {
            Email = "  ALICE@aistudyhub.local  ",
            Username = "alice",
            FullName = "  Alice A.  ",
            Password = "Password!1",
        },
        userAgent: "nunit",
        ipAddress: "127.0.0.1");

    // ── ASSERT (response shape) ──────────────────────────────────────
    result.AccessToken.Should().Be("access.jwt.token");
    result.RefreshToken.Should().Be("rt-abc");
    result.TokenType.Should().Be("Bearer");
    result.ExpiresIn.Should().Be(3600);
    result.User.Email.Should().Be("alice@aistudyhub.local");
    result.User.Username.Should().Be("alice");
    result.User.FullName.Should().Be("Alice A.");
    result.User.Role.Should().Be(Role.StudentRoleName);
    result.User.IsActive.Should().BeTrue();

    // ── ASSERT (side effect — profile mirrored) ──────────────────────
    var profile = await db.Users.Include(u => u.Role).SingleAsync();
    profile.SupabaseUserId.Should().Be(supabaseUserId);
    profile.Username.Should().Be("alice");
    profile.FullName.Should().Be("Alice A.");
    profile.Role.RoleName.Should().Be(Role.StudentRoleName);
    profile.TotalTokensUsed.Should().Be(0);
    profile.IsActive.Should().BeTrue();

    // ── ASSERT (mock interaction) ────────────────────────────────────
    gotrue.VerifyAll();
}
```

Source: `AI_Study_Hub_v2.Tests/Services/SupabaseAuthServiceTests.cs:46-98`

### 3.5 Walkthrough — giải thích từng phần

**Arrange (4 việc):**

1. `using var db = TestDb.CreateInMemory();`
   `using` → DbContext bị dispose tự động cuối test, không leak. `TestDb.CreateInMemory()` tạo DB mới + seed 2 role.

2. `var supabaseUserId = Guid.NewGuid();` — giả lập ID GoTrue trả về cho user vừa tạo.

3. `BuildSession(...)` — helper private build `GoTrueSession` với access/refresh token cố định để assert được giá trị chính xác.

4. **Mock setup quan trọng nhất:**
   ```csharp
   var gotrue = new Mock<IGoTrueClient>(MockBehavior.Strict);
   gotrue.Setup(g => g.SignUpAsync(
       "alice@aistudyhub.local",     // ← email phải đã trim + lowercase
       "Password!1",
       It.Is<Dictionary<string, object?>>(m =>
           (string)m["username"]! == "alice"
           && (string)m["full_name"]! == "Alice A."),  // ← FullName phải đã trim
       It.IsAny<CancellationToken>()))
       .ReturnsAsync(session);
   ```
   - **`MockBehavior.Strict`:** nếu service gọi mock với args KHÁC setup → exception. Đây là cách mạnh để verify service không gọi GoTrue với data raw (chưa trim).
   - **`It.Is<...>(m => ...)`:** matcher dạng predicate — chỉ match khi metadata dict có đúng `username` + `full_name` đã được normalize.
   - **`It.IsAny<CancellationToken>()`:** không quan tâm token, miễn là method được gọi.

**Act (1 dòng):**
```csharp
var result = await sut.RegisterAsync(request, "nunit", "127.0.0.1");
```
Input cố tình bẩn (`"  ALICE@aistudyhub.local  "`) để verify service tự normalize trước khi gọi GoTrue.

**Assert (3 nhóm):**

1. **Response shape** — output trả ra client có đúng không.
2. **Side effect** — record được persist vào `public.users` chưa, link đúng `SupabaseUserId` chưa, gắn đúng Role `Student` chưa.
3. **Mock interaction** — `gotrue.VerifyAll()` xác nhận mọi setup `Strict` đều đã được gọi. Nếu service quên không gọi `SignUpAsync` → fail.

> **Câu chốt:** test này KHÔNG cần Postgres, KHÔNG cần Supabase Local stack, KHÔNG cần network. Chạy được trên máy CI/CD của hội đồng kể cả khi không có Docker.

### 3.6 Live run

```powershell
dotnet test AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/AI_Study_Hub_v2.Tests.csproj `
            --filter "FullyQualifiedName~RegisterAsync_HappyPath" --nologo
```

Expected: 1/1 passed. Cho audience xem output.

---

## 4. Demo 2 — Controller layer: `Login_WhenServiceThrowsAuthException` (mock IAuthService + stub HttpContext)

**File:** `AI_Study_Hub_v2.Tests/Controllers/AuthControllerTests.cs:114-130` (test method).
**Helpers cùng file:** `BuildSut` (line 23-45), `StubAuthService` (line 47-70), `Principal` (line 72-73).

### 4.1 Bối cảnh

`AuthController` có **2 việc duy nhất**, business logic đẩy hết xuống `IAuthService`:

1. **Đọc claim** từ `HttpContext.User` (sub + email) rồi truyền xuống service.
2. **Map exception** từ service → HTTP response: `AuthException` → status code + `ApiErrorResponse` (`code` + `message`); `Exception` lạ khác → 500 + `code = "unexpected_error"`.

Demo 2 focus đúng việc số 2 — chứng minh khi service throw `AuthException(401, "invalid_credentials", ...)` thì controller trả đúng 401 + body `ApiErrorResponse { Code = "invalid_credentials", Message = "Email or password is incorrect." }`.

**Tại sao test method này?**
- Cover layer khác Demo 1 (controller, không phải service)
- Demo cách **stub HttpContext** — kỹ thuật quan trọng khi unit test ASP.NET Core controller, hội đồng hay hỏi
- Demo cách assert **action result + body** (`ObjectResult`, status code, value type)
- Pair tự nhiên với Demo 1: Demo 1 = service throw exception → Demo 2 = controller catch + map đúng

### 4.2 Test case — bảng đặc tả

| Field | Value |
|---|---|
| ID | CTRL-Login-02 |
| Test | `Login_WhenServiceThrowsAuthException_ReturnsMappedStatusAndCode` |
| Layer | Controller (HTTP boundary) |
| SUT | `AuthController.Login` |
| Mock | `IAuthService` (Loose mặc định) |
| Stub | `IAuthenticationService` (qua `StubAuthService` — không cần cho test này nhưng `BuildSut` luôn cài) |
| Pre-conditions | Không cần DB, không cần GoTrue, không cần auth thật |

### 4.3 Scenario

**Given:**
- Mock `IAuthService.LoginAsync` được setup throw `AuthException(401, "invalid_credentials", "Email or password is incorrect.")`
- Controller được build qua `BuildSut(svc.Object)` — HttpContext giả lập, không user, không Bearer header

**When:**
- Gọi `sut.Login(new LoginRequest { Email = "a@x.com", Password = "p" }, CancellationToken.None)`

**Then (expected):**
- Action result là `ObjectResult` (KHÔNG phải `OkObjectResult`)
- `obj.StatusCode == 401`
- `obj.Value` là `ApiErrorResponse` với:
  - `Code == "invalid_credentials"`
  - `Message == "Email or password is incorrect."`

> **Edge case bonus:** controller đáng lẽ phải catch `AuthException` (từ service) ở 1 chỗ duy nhất rồi build `ObjectResult` với đúng `StatusCode` + `Code` + `Message` lấy từ exception. Nếu code controller quên `try/catch`, exception bubble lên ASP.NET runtime → 500 InternalServerError thay vì 401 → test này fail. Đây là **lý do tồn tại** của test.

### 4.4 Code thật (paste từ project)

**Helper `BuildSut` (build controller với HttpContext giả):**

```csharp
private static AuthController BuildSut(
    IAuthService service,
    ClaimsPrincipal? user = null,
    string? bearerHeader = null,
    string? savedAccessToken = null)
{
    var ctrl = new AuthController(service, NullLogger<AuthController>.Instance);
    var http = new DefaultHttpContext();

    // HttpContext.GetTokenAsync() delegates to IAuthenticationService.AuthenticateAsync
    // → stub the service so the controller's logout path can ask for the saved access_token
    var services = new ServiceCollection();
    services.AddSingleton<IAuthenticationService>(new StubAuthService(savedAccessToken));
    http.RequestServices = services.BuildServiceProvider();

    if (user is not null) http.User = user;
    if (!string.IsNullOrEmpty(bearerHeader)) http.Request.Headers.Authorization = bearerHeader;

    ctrl.ControllerContext = new ControllerContext { HttpContext = http };
    return ctrl;
}
```

Source: `AI_Study_Hub_v2.Tests/Controllers/AuthControllerTests.cs:23-45`

**Test case (full):**

```csharp
[Test]
public async Task Login_WhenServiceThrowsAuthException_ReturnsMappedStatusAndCode()
{
    // ── ARRANGE ──────────────────────────────────────────────────────
    var svc = new Mock<IAuthService>();
    svc.Setup(s => s.LoginAsync(
            It.IsAny<LoginRequest>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
       .ThrowsAsync(new AuthException(401, "invalid_credentials", "Email or password is incorrect."));

    var sut = BuildSut(svc.Object);

    // ── ACT ──────────────────────────────────────────────────────────
    var actionResult = await sut.Login(
        new LoginRequest { Email = "a@x.com", Password = "p" },
        CancellationToken.None);

    // ── ASSERT (action result + status code) ─────────────────────────
    var obj = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
    obj.StatusCode.Should().Be(401);

    // ── ASSERT (body shape — ApiErrorResponse với code + message đúng)
    var err = obj.Value.Should().BeOfType<ApiErrorResponse>().Subject;
    err.Code.Should().Be("invalid_credentials");
    err.Message.Should().Be("Email or password is incorrect.");
}
```

Source: `AI_Study_Hub_v2.Tests/Controllers/AuthControllerTests.cs:114-130`

### 4.5 Walkthrough — giải thích từng phần

**Arrange (2 việc):**

1. **Mock service throw `AuthException`:**
   ```csharp
   svc.Setup(...).ThrowsAsync(new AuthException(401, "invalid_credentials", "..."));
   ```
   Đây là cách Moq giả lập "service xử lý xong → quyết định ném exception vì pwd sai". Note `MockBehavior` mặc định là `Loose` (không setup thì trả default), khác Demo 1 dùng `Strict`. Ở đây Loose là phù hợp vì controller chỉ gọi đúng `LoginAsync`, không cần verify call khác.

2. **`BuildSut(svc.Object)`** — build controller với:
   - `IAuthService` đã mock (kẻ ném exception)
   - HttpContext mặc định, không user (`http.User` rỗng OK vì endpoint Login `[AllowAnonymous]`)
   - Không Bearer header (Login không cần)
   - StubAuthService cài sẵn để controller có thể gọi `HttpContext.GetTokenAsync()` mà không crash (dù Login không dùng)

**Act (1 dòng):**
```csharp
var actionResult = await sut.Login(loginRequest, CancellationToken.None);
```
Trả về `ActionResult<AuthResponse>` — kiểu generic của ASP.NET Core, có 2 nhánh: `Result` (IActionResult) hoặc `Value` (AuthResponse trực tiếp). Vì service throw → controller catch → build `ObjectResult` → set vào `actionResult.Result`.

**Assert (2 tầng):**

1. **Action result type + status code:**
   ```csharp
   var obj = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
   obj.StatusCode.Should().Be(401);
   ```
   `BeOfType<ObjectResult>()` xác nhận controller trả `ObjectResult` (KHÔNG phải `OkObjectResult` cho 200, cũng KHÔNG phải `BadRequestObjectResult` cho 400). `.Subject` lấy ra reference đã cast → đọc tiếp `StatusCode`.

2. **Body shape:**
   ```csharp
   var err = obj.Value.Should().BeOfType<ApiErrorResponse>().Subject;
   err.Code.Should().Be("invalid_credentials");
   err.Message.Should().Be("Email or password is incorrect.");
   ```
   Verify body là `ApiErrorResponse` (DTO chuẩn cho lỗi của API), với `Code` + `Message` lấy nguyên từ `AuthException`. Nghĩa là controller KHÔNG được phép tự nghĩ ra message khác — phải pass-through từ service xuống client.

> **Câu chốt:** test này verify "contract HTTP" của controller. Service muốn report 401 → client phải nhận đúng 401 + đúng error code. Nếu sau này có ai đó thêm `try/catch` "tự ái" trong controller → swallow exception → trả 200 với body lạ → test này fail ngay.

### 4.6 Live run

```powershell
dotnet test AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/AI_Study_Hub_v2.Tests.csproj `
            --filter "FullyQualifiedName~Login_WhenServiceThrowsAuthException" --nologo
```

Expected: 1/1 passed. Cho audience xem output.

---

## 5. Final live run — full suite

```powershell
dotnet test D:\FPT\summer2026\SWP391\AI_Study_Hub_v2\AI_Study_Hub_v2.Tests\AI_Study_Hub_v2.Tests.csproj --nologo
```

Expected:
```
Passed!  - Failed: 0, Passed: 38, Skipped: 0, Total: 38, Duration: ~2 s
  ├─ SmokeTests:                  3 (pipeline sanity)
  ├─ SupabaseAuthServiceTests:   18 (Register/Login/Refresh/Logout/Me happy + error paths)
  └─ AuthControllerTests:        17 (claim parsing + AuthException mapping + Bearer header)
```

Chạy live cho hội đồng thấy. Đây là moment "show, don't tell".

---

## 6. Q&A — câu hội đồng hay hỏi + đáp án sẵn

**Q1. Tại sao chọn NUnit thay vì xUnit?**
> Cú pháp NUnit gần JUnit (Java) — đa số thành viên đã học Java SWP. Setup/Teardown rõ ràng qua attribute, không phải dùng constructor/Dispose pattern như xUnit. Tài liệu Việt cũng nhiều hơn. Còn sức mạnh thì cả 2 ngang ngửa.

**Q2. Tại sao dùng EF Core InMemory thay vì test Postgres thật?**
> Vì đây là **unit test**, không phải integration test. Unit test mục tiêu là isolate business logic của 1 method, không phải test DB driver. EF Core InMemory chạy trong RAM, ~30ms/test, không cần Docker. Integration test với Postgres thật là Phase sau (xem `02_Resume_Pack.md` — đã quyết skip cho Phase 1 do ROI thấp).

**Q3. Mock vs Stub vs Fake khác nhau gì? Demo này dùng cái nào?**
> - **Stub** (như `StubAuthService` ở `AuthControllerTests.cs:47-70`): trả về giá trị hard-code, không verify call. Demo 2 dùng để giả lập `IAuthenticationService` cho `HttpContext.GetTokenAsync()`.
> - **Mock** (như `Mock<IGoTrueClient>` ở Demo 1, `Mock<IAuthService>` ở Demo 2): có verify call (số lần, args). Dùng thư viện Moq.
> - **Fake** (như `AppDbContext` qua InMemory provider ở Demo 1): bản triển khai thật nhưng đơn giản, dùng được trong test.
> Trong demo, ta dùng cả 3.

**Q4. Tại sao `MockBehavior.Strict` ở Demo 1 nhưng không dùng ở Demo 2?**
> Strict mode → nếu code gọi method KHÔNG có trong setup → throw exception ngay. Tốt cho test "cách service tương tác với dependency" (Demo 1 — verify đúng metadata trim được pass cho GoTrue). Demo 2 dùng Loose (default) vì controller chỉ gọi đúng `LoginAsync` và mình quan tâm output (status code + body), không cần fail-fast nếu thừa setup.

**Q5. Test Demo 2 không dùng đến Bearer header / `ClaimsPrincipal` thật, sao vẫn cần stub `IAuthenticationService`?**
> `BuildSut` luôn cài `StubAuthService` vào `HttpContext.RequestServices` để các test khác (như `Logout`) dùng được `HttpContext.GetTokenAsync()`. Test `Login_WhenServiceThrowsAuthException` không trigger code path đó nên stub có hay không cũng không ảnh hưởng — đây là **scaffolding chia sẻ**, viết 1 lần cho mọi controller test.

**Q6. Coverage bao nhiêu %?**
> Phase 1 chưa đo formal. 38 test cover đủ 5 luồng auth (Register/Login/Refresh/Logout/Me) cả happy + error path + claim parsing + status code mapping. Có thể thêm `coverlet` collect coverage báo cáo nếu hội đồng yêu cầu (~5 phút bật).

**Q7. Test có chạy được trên CI/CD không?**
> Có. `dotnet test` chỉ cần .NET SDK 8, không cần Docker / Postgres / network. Chạy được trên GitHub Actions, GitLab CI, Azure Pipelines, Jenkins. Đây chính là lý do thiết kế test offline-first.

**Q8. Nếu Supabase Local stack đã `docker compose down`, test còn chạy được không?**
> Có. 38/38 vẫn pass. Đã verify trong session A (xem `02_Resume_Pack.md` Section 3.2). Đây là điểm mạnh của unit test so với smoke curl ở Section 8 file 02.

**Q9. AAA pattern là gì?**
> **A**rrange (chuẩn bị input + mock) → **A**ct (gọi method cần test) → **A**ssert (kiểm kết quả + side effect + interaction). Mọi unit test tốt đều theo. Cả Demo 1 và Demo 2 đều tách 3 phần bằng comment để audience nhìn thấy rõ.

**Q10. Test có chạy parallel không?**
> NUnit mặc định chạy serial trong cùng `[TestFixture]`. Cho parallel cần thêm `[Parallelizable(ParallelScope.All)]`. Hiện tại serial vì test rất nhanh (~2s cho 38 test) — chưa cần optimize.

**Q11. Tại sao Demo 1 ở Service layer, Demo 2 ở Controller layer? Sao không cùng 1 layer?**
> Để show được cả 2 kỹ thuật unit test phổ biến nhất khi làm ASP.NET Core:
> - **Service layer** (Demo 1): mock infrastructure (HTTP client, DB) → test business logic thuần.
> - **Controller layer** (Demo 2): mock service + stub HttpContext → test contract HTTP (status code, body shape, exception mapping).
> Hội đồng thấy được cách team approach test có chiều sâu, không chỉ test 1 chỗ.

---

## 7. Phụ lục — file cheat sheet

| Cần | Lệnh / Path |
|---|---|
| Run all tests | `dotnet test AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/AI_Study_Hub_v2.Tests.csproj --nologo` |
| Run 1 fixture | `--filter "FullyQualifiedName~SupabaseAuthServiceTests"` |
| Run 1 test (Demo 1) | `--filter "FullyQualifiedName~RegisterAsync_HappyPath"` |
| Run 1 test (Demo 2) | `--filter "FullyQualifiedName~Login_WhenServiceThrowsAuthException"` |
| Run + verbose | `-v normal` |
| Run + coverage | `--collect:"XPlat Code Coverage"` |
| File csproj | `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/AI_Study_Hub_v2.Tests.csproj` |
| File smoke test | `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/SmokeTests.cs` |
| File Demo 1 (service) | `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Services/SupabaseAuthServiceTests.cs:46-98` |
| File Demo 2 (controller) | `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Controllers/AuthControllerTests.cs:114-130` |
| Helper InMemory DB | `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Support/TestDb.cs` |
| Helper BuildSut + StubAuthService | `AI_Study_Hub_v2/AI_Study_Hub_v2.Tests/Controllers/AuthControllerTests.cs:23-73` |

---

**END.** File này độc lập với 01-08. Khi nào team thay đổi test (add/xoá), update lại Section 5 (count) + Section 3-4 (nếu `RegisterAsync_HappyPath` hoặc `Login_WhenServiceThrowsAuthException` bị refactor).
