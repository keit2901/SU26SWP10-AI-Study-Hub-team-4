using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

/// <summary>
/// Tests cho <see cref="AuthController"/> — focus vào 2 việc controller thực sự làm:
/// (1) đọc claim từ <see cref="ClaimsPrincipal"/> để truyền xuống service,
/// (2) map <see cref="AuthException"/> ra <see cref="ApiErrorResponse"/> + status code đúng.
/// Service được mock — flow business logic đã có coverage riêng ở <c>SupabaseAuthServiceTests</c>.
/// </summary>
[TestFixture]
public class AuthControllerTests
{
    private static AuthController BuildSut(
        IAuthService service,
        ClaimsPrincipal? user = null,
        string? bearerHeader = null,
        string? savedAccessToken = null,
        IRecaptchaVerificationService? recaptcha = null)
    {
        recaptcha ??= PassingRecaptcha();
        var ctrl = new AuthController(service, recaptcha, NullLogger<AuthController>.Instance);
        var http = new DefaultHttpContext();

        // HttpContext.GetTokenAsync() delegates to IAuthenticationService.AuthenticateAsync
        // and reads the token from AuthenticationProperties. We stub the service so the
        // controller's logout path can ask for the saved access_token without DI scaffolding.
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new StubAuthService(savedAccessToken));
        http.RequestServices = services.BuildServiceProvider();

        if (user is not null)
        {
            http.User = user;
        }
        if (!string.IsNullOrEmpty(bearerHeader))
        {
            http.Request.Headers.Authorization = bearerHeader;
        }
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    private sealed class StubAuthService : IAuthenticationService
    {
        private readonly string? _savedAccessToken;

        public StubAuthService(string? savedAccessToken) => _savedAccessToken = savedAccessToken;

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            if (_savedAccessToken is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var props = new AuthenticationProperties();
            props.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = _savedAccessToken } });
            var ticket = new AuthenticationTicket(context.User ?? new ClaimsPrincipal(new ClaimsIdentity()), props, scheme ?? "Bearer");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Bearer"));

    private static IRecaptchaVerificationService PassingRecaptcha()
    {
        var recaptcha = new Mock<IRecaptchaVerificationService>();
        recaptcha.SetupGet(t => t.IsEnabled).Returns(false);
        recaptcha.SetupGet(t => t.IsConfigured).Returns(false);
        recaptcha.SetupGet(t => t.ShouldVerify).Returns(false);
        recaptcha.Setup(t => t.VerifyAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecaptchaVerificationResult.Valid());
        return recaptcha.Object;
    }

    private static IRecaptchaVerificationService FailingRecaptcha()
    {
        var recaptcha = new Mock<IRecaptchaVerificationService>();
        recaptcha.SetupGet(t => t.IsEnabled).Returns(true);
        recaptcha.SetupGet(t => t.IsConfigured).Returns(true);
        recaptcha.SetupGet(t => t.ShouldVerify).Returns(true);
        recaptcha.Setup(t => t.VerifyAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecaptchaVerificationResult.Invalid("reCAPTCHA failed.", new[] { "missing-input-response" }));
        return recaptcha.Object;
    }

    private static AuthResponse SampleAuthResponse(string username = "alice", string role = "Student") => new()
    {
        AccessToken = "access.jwt",
        RefreshToken = "rt-1",
        TokenType = "Bearer",
        ExpiresIn = 3600,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        User = new UserDto
        {
            Id = Guid.NewGuid(),
            Email = "alice@x.com",
            Username = username,
            FullName = "Alice",
            Role = role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        }
    };

    // -------------------------------------------------------------------------
    // Login / Register / Refresh — happy path + AuthException mapping
    // -------------------------------------------------------------------------

    [Test]
    public async Task Login_Returns200_WithAuthResponse()
    {
        var svc = new Mock<IAuthService>();
        svc.Setup(s => s.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(SampleAuthResponse());

        var sut = BuildSut(svc.Object);

        var actionResult = await sut.Login(new LoginRequest { Email = "a@x.com", Password = "p" }, CancellationToken.None);

        var ok = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeOfType<AuthResponse>().Which.AccessToken.Should().Be("access.jwt");
    }

    [Test]
    public async Task Login_WhenRecaptchaFails_Returns400_AndDoesNotCallAuthService()
    {
        var svc = new Mock<IAuthService>(MockBehavior.Strict);
        var sut = BuildSut(svc.Object, recaptcha: FailingRecaptcha());

        var actionResult = await sut.Login(new LoginRequest { Email = "a@x.com", Password = "p" }, CancellationToken.None);

        var badRequest = actionResult.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.Code.Should().Be("recaptcha_failed");
        error.Errors.Should().ContainKey("recaptcha");
    }

    [Test]
    public async Task Login_WhenServiceThrowsAuthException_ReturnsMappedStatusAndCode()
    {
        var svc = new Mock<IAuthService>();
        svc.Setup(s => s.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new AuthException(401, "invalid_credentials", "Email or password is incorrect."));

        var sut = BuildSut(svc.Object);

        var actionResult = await sut.Login(new LoginRequest { Email = "a@x.com", Password = "p" }, CancellationToken.None);

        var obj = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(401);
        var err = obj.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        err.Code.Should().Be("invalid_credentials");
        err.Message.Should().Be("Email or password is incorrect.");
    }

    [Test]
    public async Task Register_WhenRecaptchaFails_Returns400_AndDoesNotCallAuthService()
    {
        var svc = new Mock<IAuthService>(MockBehavior.Strict);
        var sut = BuildSut(svc.Object, recaptcha: FailingRecaptcha());

        var actionResult = await sut.Register(
            new RegisterRequest { Email = "a@x.com", Username = "alice", FullName = "Alice", Password = "Password!1" },
            CancellationToken.None);

        var badRequest = actionResult.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("recaptcha_failed");
    }

    [Test]
    public async Task Register_WhenServiceThrowsAuthException_Returns409_UsernameTaken()
    {
        var svc = new Mock<IAuthService>();
        svc.Setup(s => s.RegisterAsync(It.IsAny<RegisterRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new AuthException(409, "username_taken", "Username is already taken."));

        var sut = BuildSut(svc.Object);

        var actionResult = await sut.Register(
            new RegisterRequest { Email = "a@x.com", Username = "alice", FullName = "Alice", Password = "Password!1" },
            CancellationToken.None);

        var obj = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(409);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("username_taken");
    }

    [Test]
    public async Task Refresh_WhenServiceThrowsAuthException_Returns401_InvalidRefreshToken()
    {
        var svc = new Mock<IAuthService>();
        svc.Setup(s => s.RefreshAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new AuthException(401, "invalid_refresh_token", "Refresh token is invalid or has been revoked."));

        var sut = BuildSut(svc.Object);

        var actionResult = await sut.Refresh(new RefreshTokenRequest { RefreshToken = "rt-bad" }, CancellationToken.None);

        var obj = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(401);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("invalid_refresh_token");
    }

    [Test]
    public async Task Login_WhenServiceThrowsUnexpected_Returns500_UnexpectedError()
    {
        var svc = new Mock<IAuthService>();
        svc.Setup(s => s.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("kaboom"));

        var sut = BuildSut(svc.Object);

        var actionResult = await sut.Login(new LoginRequest { Email = "a@x.com", Password = "p" }, CancellationToken.None);

        var obj = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(500);
        var err = obj.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        err.Code.Should().Be("unexpected_error");
    }

    // -------------------------------------------------------------------------
    // Me — claim parsing (sub + email)
    // -------------------------------------------------------------------------

    [Test]
    public async Task Me_PassesSupabaseUserIdAndEmailFromClaims_ToService()
    {
        var supabaseUserId = Guid.NewGuid();
        var svc = new Mock<IAuthService>();
        Guid? capturedId = null;
        string? capturedEmail = null;
        svc.Setup(s => s.GetCurrentUserAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .Callback<Guid, string?, CancellationToken>((id, email, _) =>
           {
               capturedId = id;
               capturedEmail = email;
           })
           .ReturnsAsync(new UserDto { Id = Guid.NewGuid(), Email = "admin@x.com", Username = "admin", Role = "Admin", IsActive = true });

        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, supabaseUserId.ToString()),
            new Claim(ClaimTypes.Email, "admin@x.com"));

        var sut = BuildSut(svc.Object, user: principal);

        var actionResult = await sut.Me(CancellationToken.None);

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        capturedId.Should().Be(supabaseUserId);
        capturedEmail.Should().Be("admin@x.com");
    }

    [Test]
    public async Task Me_FallsBackToRawSubClaim_WhenNameIdentifierMissing()
    {
        // GoTrue tokens may surface only the raw "sub" claim depending on JwtBearer config.
        var supabaseUserId = Guid.NewGuid();
        var svc = new Mock<IAuthService>();
        Guid? capturedId = null;
        svc.Setup(s => s.GetCurrentUserAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .Callback<Guid, string?, CancellationToken>((id, _, _) => capturedId = id)
           .ReturnsAsync(new UserDto());

        var principal = Principal(new Claim("sub", supabaseUserId.ToString()));
        var sut = BuildSut(svc.Object, user: principal);

        var actionResult = await sut.Me(CancellationToken.None);

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        capturedId.Should().Be(supabaseUserId);
    }

    [Test]
    public async Task Me_FallsBackToRawEmailClaim_WhenClaimTypesEmailMissing()
    {
        var supabaseUserId = Guid.NewGuid();
        var svc = new Mock<IAuthService>();
        string? capturedEmail = null;
        svc.Setup(s => s.GetCurrentUserAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .Callback<Guid, string?, CancellationToken>((_, email, _) => capturedEmail = email)
           .ReturnsAsync(new UserDto());

        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, supabaseUserId.ToString()),
            new Claim("email", "raw@x.com"));

        var sut = BuildSut(svc.Object, user: principal);

        await sut.Me(CancellationToken.None);

        capturedEmail.Should().Be("raw@x.com");
    }

    [Test]
    public async Task Me_WhenSubClaimMissing_Returns401_MissingUserId()
    {
        var svc = new Mock<IAuthService>(MockBehavior.Strict);
        var principal = Principal(); // no claims
        var sut = BuildSut(svc.Object, user: principal);

        var actionResult = await sut.Me(CancellationToken.None);

        var obj = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(401);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("missing_user_id");
        svc.Verify(s => s.GetCurrentUserAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Me_WhenSubClaimNotGuid_Returns401_MissingUserId()
    {
        var svc = new Mock<IAuthService>(MockBehavior.Strict);
        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, "not-a-guid"));
        var sut = BuildSut(svc.Object, user: principal);

        var actionResult = await sut.Me(CancellationToken.None);

        var obj = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(401);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("missing_user_id");
    }

    [Test]
    public async Task Me_WhenServiceThrowsUserNotFound_Returns404()
    {
        var supabaseUserId = Guid.NewGuid();
        var svc = new Mock<IAuthService>();
        svc.Setup(s => s.GetCurrentUserAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new AuthException(404, "user_not_found", "User profile not found."));

        var principal = Principal(new Claim(ClaimTypes.NameIdentifier, supabaseUserId.ToString()));
        var sut = BuildSut(svc.Object, user: principal);

        var actionResult = await sut.Me(CancellationToken.None);

        var obj = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(404);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("user_not_found");
    }

    // -------------------------------------------------------------------------
    // Logout — Authorization header parsing + service call
    // -------------------------------------------------------------------------

    [Test]
    public async Task Logout_WithBearerHeader_ExtractsTokenAndReturns204()
    {
        var svc = new Mock<IAuthService>();
        string? capturedToken = null;
        svc.Setup(s => s.LogoutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Callback<string, CancellationToken>((t, _) => capturedToken = t)
           .Returns(Task.CompletedTask);

        var sut = BuildSut(svc.Object, bearerHeader: "Bearer abc.def.ghi");

        var actionResult = await sut.Logout(CancellationToken.None);

        actionResult.Should().BeOfType<NoContentResult>();
        capturedToken.Should().Be("abc.def.ghi");
    }

    [Test]
    public async Task Logout_WithBearerHeaderCaseInsensitive_StillExtractsToken()
    {
        var svc = new Mock<IAuthService>();
        string? capturedToken = null;
        svc.Setup(s => s.LogoutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Callback<string, CancellationToken>((t, _) => capturedToken = t)
           .Returns(Task.CompletedTask);

        var sut = BuildSut(svc.Object, bearerHeader: "bearer xyz.token");

        var actionResult = await sut.Logout(CancellationToken.None);

        actionResult.Should().BeOfType<NoContentResult>();
        capturedToken.Should().Be("xyz.token");
    }

    [Test]
    public async Task Logout_WithoutAuthorizationHeader_Returns401_MissingAccessToken()
    {
        var svc = new Mock<IAuthService>(MockBehavior.Strict);
        var sut = BuildSut(svc.Object); // no header

        var actionResult = await sut.Logout(CancellationToken.None);

        var obj = actionResult.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(401);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("missing_access_token");
    }

    [Test]
    public async Task Logout_WhenServiceThrowsUnexpected_Returns500_UnexpectedError()
    {
        var svc = new Mock<IAuthService>();
        svc.Setup(s => s.LogoutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("kaboom"));

        var sut = BuildSut(svc.Object, bearerHeader: "Bearer abc");

        var actionResult = await sut.Logout(CancellationToken.None);

        var obj = actionResult.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(500);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("unexpected_error");
    }
}
