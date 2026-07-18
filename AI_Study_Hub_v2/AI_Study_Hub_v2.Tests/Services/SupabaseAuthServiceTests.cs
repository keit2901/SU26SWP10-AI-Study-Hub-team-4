using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

/// <summary>
/// Demo coverage cho 5 luồng auth (Register / Login / Refresh / Logout / Me).
/// Mock <see cref="IGoTrueClient"/>, dùng EF Core InMemory cho <c>public.users</c> +
/// <c>public.roles</c>. Không đụng tới Postgres thật, không đụng GoTrue thật —
/// chạy được offline kể cả khi Supabase Local stack đã <c>docker compose down</c>.
/// </summary>
[TestFixture]
public class SupabaseAuthServiceTests
{
    private static SupabaseAuthService BuildSut(Data.AppDbContext db, IGoTrueClient gotrue) =>
        new(db, gotrue, Mock.Of<IRegistrationCoordinator>(), NullLogger<SupabaseAuthService>.Instance, Mock.Of<IAuditLogService>());

    private static GoTrueSession BuildSession(Guid userId, string email, string accessToken = "access.jwt.token", string refreshToken = "rt-abc", int expiresIn = 3600) =>
        new()
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = "bearer",
            ExpiresIn = expiresIn,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeSeconds(),
            User = new GoTrueUser
            {
                Id = userId,
                Email = email,
                Audience = "authenticated",
                Role = "authenticated",
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };

    // -------------------------------------------------------------------------
    // RegisterAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task RegisterAsync_HappyPath_CreatesGoTrueIdentity_AndMirrorsProfile_AsStudent()
    {
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

        // Response shape
        result.AccessToken.Should().Be("access.jwt.token");
        result.RefreshToken.Should().Be("rt-abc");
        result.TokenType.Should().Be("Bearer");
        result.ExpiresIn.Should().Be(3600);
        result.User.Email.Should().Be("alice@aistudyhub.local");
        result.User.Username.Should().Be("alice");
        result.User.FullName.Should().Be("Alice A.");
        result.User.Role.Should().Be(Role.StudentRoleName);
        result.User.IsActive.Should().BeTrue();

        // Profile mirrored
        var profile = await db.Users.Include(u => u.Role).SingleAsync();
        profile.SupabaseUserId.Should().Be(supabaseUserId);
        profile.Username.Should().Be("alice");
        profile.FullName.Should().Be("Alice A.");
        profile.Role.RoleName.Should().Be(Role.StudentRoleName);
        profile.TotalTokensUsed.Should().Be(0);
        profile.IsActive.Should().BeTrue();

        gotrue.VerifyAll();
    }

    [Test]
    public async Task RegisterAsync_UsernameTaken_Throws409_AndDoesNotCallGoTrue()
    {
        using var db = TestDb.CreateInMemory();
        // pre-seed an existing user with the same username
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Username = "alice",
            FullName = "Existing",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var gotrue = new Mock<IGoTrueClient>(MockBehavior.Strict);
        var sut = BuildSut(db, gotrue.Object);

        var act = () => sut.RegisterAsync(
            new RegisterRequest { Email = "alice@x.com", Username = "alice", FullName = "Alice", Password = "Password!1" },
            null, null);

        var ex = await act.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(409);
        ex.Which.Code.Should().Be("username_taken");
        gotrue.Verify(g => g.SignUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task RegisterAsync_StudentRoleMissing_Throws500_RoleNotSeeded()
    {
        using var db = TestDb.CreateInMemory(seedRoles: false);
        var gotrue = new Mock<IGoTrueClient>(MockBehavior.Strict);
        var sut = BuildSut(db, gotrue.Object);

        var act = () => sut.RegisterAsync(
            new RegisterRequest { Email = "a@x.com", Username = "alice", FullName = "Alice", Password = "Password!1" },
            null, null);

        var ex = await act.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(500);
        ex.Which.Code.Should().Be("role_not_seeded");
    }

    [Test]
    public async Task RegisterAsync_GoTrueReturnsNullUser_Throws500_GoTrueNoUser()
    {
        using var db = TestDb.CreateInMemory();
        var gotrue = new Mock<IGoTrueClient>();
        gotrue
            .Setup(g => g.SignUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoTrueSession { AccessToken = "x", RefreshToken = "y", User = null });

        var sut = BuildSut(db, gotrue.Object);

        var act = () => sut.RegisterAsync(
            new RegisterRequest { Email = "a@x.com", Username = "alice", FullName = "Alice", Password = "Password!1" },
            null, null);

        var ex = await act.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(500);
        ex.Which.Code.Should().Be("gotrue_no_user");

        // No profile should have been persisted.
        (await db.Users.CountAsync()).Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // LoginAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task LoginAsync_HappyPath_ReturnsAuthResponse_WithEmailFromSession()
    {
        using var db = TestDb.CreateInMemory();
        var supabaseUserId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            RoleId = 1,
            SupabaseUserId = supabaseUserId,
            Username = "admin",
            FullName = "Admin",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var gotrue = new Mock<IGoTrueClient>();
        gotrue
            .Setup(g => g.SignInWithPasswordAsync("admin@aistudyhub.local", "secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSession(supabaseUserId, "admin@aistudyhub.local"));

        var sut = BuildSut(db, gotrue.Object);

        var result = await sut.LoginAsync(
            new LoginRequest { Email = "ADMIN@aistudyhub.local", Password = "secret" },
            null, null);

        result.User.Email.Should().Be("admin@aistudyhub.local");
        result.User.Role.Should().Be(Role.AdminRoleName);
        result.User.Username.Should().Be("admin");
        result.AccessToken.Should().Be("access.jwt.token");
    }

    [Test]
    public async Task LoginAsync_GoTrueReturnsNullUser_Throws401_InvalidCredentials()
    {
        using var db = TestDb.CreateInMemory();
        var gotrue = new Mock<IGoTrueClient>();
        gotrue
            .Setup(g => g.SignInWithPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoTrueSession { AccessToken = "x", RefreshToken = "y", User = null });

        var sut = BuildSut(db, gotrue.Object);

        var act = () => sut.LoginAsync(new LoginRequest { Email = "x@x.com", Password = "p" }, null, null);

        var ex = await act.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.Code.Should().Be("invalid_credentials");
    }

    [Test]
    public async Task LoginAsync_NoProfileInPublicUsers_Throws409_ProfileMissing()
    {
        // GoTrue identity exists but public.users mirror does not — admin-inserted out-of-band scenario.
        using var db = TestDb.CreateInMemory();
        var supabaseUserId = Guid.NewGuid();

        var gotrue = new Mock<IGoTrueClient>();
        gotrue
            .Setup(g => g.SignInWithPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSession(supabaseUserId, "ghost@x.com"));

        var sut = BuildSut(db, gotrue.Object);

        var act = () => sut.LoginAsync(new LoginRequest { Email = "ghost@x.com", Password = "p" }, null, null);

        var ex = await act.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(409);
        ex.Which.Code.Should().Be("profile_missing");
    }

    [Test]
    public async Task LoginAsync_UserInactive_Throws403_UserInactive()
    {
        using var db = TestDb.CreateInMemory();
        var supabaseUserId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = supabaseUserId,
            Username = "banned",
            FullName = "Banned User",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var gotrue = new Mock<IGoTrueClient>();
        gotrue
            .Setup(g => g.SignInWithPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSession(supabaseUserId, "banned@x.com"));

        var sut = BuildSut(db, gotrue.Object);

        var act = () => sut.LoginAsync(new LoginRequest { Email = "banned@x.com", Password = "p" }, null, null);

        var ex = await act.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(403);
        ex.Which.Code.Should().Be("user_inactive");
    }

    // -------------------------------------------------------------------------
    // RefreshAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task RefreshAsync_HappyPath_ReturnsRotatedTokens()
    {
        using var db = TestDb.CreateInMemory();
        var supabaseUserId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = supabaseUserId,
            Username = "alice",
            FullName = "Alice",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var gotrue = new Mock<IGoTrueClient>();
        gotrue
            .Setup(g => g.RefreshAsync("rt-old", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSession(supabaseUserId, "alice@x.com", accessToken: "access.new", refreshToken: "rt-new"));

        var sut = BuildSut(db, gotrue.Object);

        var result = await sut.RefreshAsync(new RefreshTokenRequest { RefreshToken = "rt-old" }, null, null);

        result.AccessToken.Should().Be("access.new");
        result.RefreshToken.Should().Be("rt-new");
        result.User.Username.Should().Be("alice");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task RefreshAsync_EmptyOrWhitespaceToken_Throws401_MissingRefreshToken(string? token)
    {
        using var db = TestDb.CreateInMemory();
        var gotrue = new Mock<IGoTrueClient>(MockBehavior.Strict);
        var sut = BuildSut(db, gotrue.Object);

        var act = () => sut.RefreshAsync(new RefreshTokenRequest { RefreshToken = token! }, null, null);

        var ex = await act.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.Code.Should().Be("missing_refresh_token");
        gotrue.Verify(g => g.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task RefreshAsync_GoTrueReturnsNullUser_Throws401_InvalidRefreshToken()
    {
        using var db = TestDb.CreateInMemory();
        var gotrue = new Mock<IGoTrueClient>();
        gotrue
            .Setup(g => g.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoTrueSession { AccessToken = "x", RefreshToken = "y", User = null });

        var sut = BuildSut(db, gotrue.Object);

        var act = () => sut.RefreshAsync(new RefreshTokenRequest { RefreshToken = "rt-bad" }, null, null);

        var ex = await act.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.Code.Should().Be("invalid_refresh_token");
    }

    // -------------------------------------------------------------------------
    // LogoutAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task LogoutAsync_HappyPath_CallsGoTrueSignOut_GlobalScope()
    {
        using var db = TestDb.CreateInMemory();
        var gotrue = new Mock<IGoTrueClient>(MockBehavior.Strict);
        gotrue
            .Setup(g => g.SignOutAsync("access.jwt.token", true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut(db, gotrue.Object);

        await sut.LogoutAsync("access.jwt.token");

        gotrue.VerifyAll();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task LogoutAsync_EmptyOrWhitespaceToken_Throws401_MissingAccessToken(string? token)
    {
        using var db = TestDb.CreateInMemory();
        var gotrue = new Mock<IGoTrueClient>(MockBehavior.Strict);
        var sut = BuildSut(db, gotrue.Object);

        var act = () => sut.LogoutAsync(token!);

        var ex = await act.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.Code.Should().Be("missing_access_token");
        gotrue.Verify(g => g.SignOutAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // GetCurrentUserAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetCurrentUserAsync_HappyPath_ReturnsUserDto_WithEmailFromClaim()
    {
        using var db = TestDb.CreateInMemory();
        var supabaseUserId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = profileId,
            RoleId = 1,
            SupabaseUserId = supabaseUserId,
            Username = "admin",
            FullName = "Default Admin",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var gotrue = new Mock<IGoTrueClient>(MockBehavior.Strict);
        var sut = BuildSut(db, gotrue.Object);

        var result = await sut.GetCurrentUserAsync(supabaseUserId, email: "admin@aistudyhub.local");

        result.Id.Should().Be(profileId);
        result.Email.Should().Be("admin@aistudyhub.local"); // D6 fix — email comes from claim, not DB
        result.Username.Should().Be("admin");
        result.FullName.Should().Be("Default Admin");
        result.Role.Should().Be(Role.AdminRoleName);
        result.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task GetCurrentUserAsync_NullEmailClaim_ReturnsEmptyEmail()
    {
        // Defensive fallback when JwtBearer didn't surface ClaimTypes.Email.
        using var db = TestDb.CreateInMemory();
        var supabaseUserId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = supabaseUserId,
            Username = "alice",
            FullName = "Alice",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var gotrue = new Mock<IGoTrueClient>(MockBehavior.Strict);
        var sut = BuildSut(db, gotrue.Object);

        var result = await sut.GetCurrentUserAsync(supabaseUserId, email: null);

        result.Email.Should().Be(string.Empty);
        result.Username.Should().Be("alice");
    }

    [Test]
    public async Task GetCurrentUserAsync_ProfileMissing_Throws404_UserNotFound()
    {
        using var db = TestDb.CreateInMemory();
        var gotrue = new Mock<IGoTrueClient>(MockBehavior.Strict);
        var sut = BuildSut(db, gotrue.Object);

        var act = () => sut.GetCurrentUserAsync(Guid.NewGuid(), email: "ghost@x.com");

        var ex = await act.Should().ThrowAsync<AuthException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("user_not_found");
    }
}
