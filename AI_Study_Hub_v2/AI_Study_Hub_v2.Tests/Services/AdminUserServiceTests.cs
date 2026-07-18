using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class AdminUserServiceTests
{
    [Test]
    public async Task UpdateQuotaAsync_AdminUpdatesQuota_PersistsAuditEvent()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var admin = SeedUser(db, roleId: 1, "Admin User");
        var student = SeedUser(db, roleId: 2, "Student User");
        var sut = new AdminUserService(db, new AuditLogService(db), StubGoTrue);

        var result = await sut.UpdateQuotaAsync(
            admin.SupabaseUserId,
            student.Id,
            75_000,
            "127.0.0.1",
            "request-1");

        result.DailyTokenQuota.Should().Be(75_000);
        (await db.Users.SingleAsync(user => user.Id == student.Id))
            .DailyTokenQuota.Should().Be(75_000);
        db.AuditLogs.Should().ContainSingle(log =>
            log.ActorUserId == admin.Id
            && log.Action == "QUOTA_UPDATE"
            && log.EntityId == student.Id.ToString());
    }

    [Test]
    public async Task UpdateQuotaAsync_NonAdminActor_Throws403()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var actor = SeedUser(db, roleId: 2, "Student Actor");
        var target = SeedUser(db, roleId: 2, "Student Target");
        var sut = new AdminUserService(db, new AuditLogService(db), StubGoTrue);

        var act = () => sut.UpdateQuotaAsync(
            actor.SupabaseUserId,
            target.Id,
            50_000,
            null,
            null);

        var exception = await act.Should().ThrowAsync<AdminException>();
        exception.Which.StatusCode.Should().Be(403);
        exception.Which.Code.Should().Be("admin_required");
    }

    [Test]
    public async Task UpdateRoleAsync_AdminAssignsModerator_PersistsRoleAndAuditEvent()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var admin = SeedUser(db, roleId: 1, "Admin User");
        var student = SeedUser(db, roleId: 2, "Student User");
        var sut = new AdminUserService(db, new AuditLogService(db), StubGoTrue);

        var result = await sut.UpdateRoleAsync(
            admin.SupabaseUserId,
            student.Id,
            Role.ModeratorRoleName,
            "127.0.0.1",
            "request-role-1");

        result.Role.Should().Be(Role.ModeratorRoleName);
        (await db.Users.Include(u => u.Role).SingleAsync(u => u.Id == student.Id))
            .Role.RoleName.Should().Be(Role.ModeratorRoleName);
        db.AuditLogs.Should().ContainSingle(log =>
            log.ActorUserId == admin.Id
            && log.Action == "ROLE_CHANGE"
            && log.EntityId == student.Id.ToString());
    }

    [Test]
    public async Task UpdateRoleAsync_CannotChangeOwnRole()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var admin = SeedUser(db, roleId: 1, "Admin User");
        var sut = new AdminUserService(db, new AuditLogService(db), StubGoTrue);

        var act = () => sut.UpdateRoleAsync(
            admin.SupabaseUserId,
            admin.Id,
            Role.StudentRoleName,
            null,
            null);

        var exception = await act.Should().ThrowAsync<AdminException>();
        exception.Which.StatusCode.Should().Be(400);
        exception.Which.Code.Should().Be("cannot_change_own_role");
    }

    [Test]
    public async Task UpdateRoleAsync_CannotAssignAdminRole()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var admin = SeedUser(db, roleId: 1, "Admin User");
        var student = SeedUser(db, roleId: 2, "Student User");
        var sut = new AdminUserService(db, new AuditLogService(db), StubGoTrue);

        var act = () => sut.UpdateRoleAsync(
            admin.SupabaseUserId,
            student.Id,
            Role.AdminRoleName,
            null,
            null);

        var exception = await act.Should().ThrowAsync<AdminException>();
        exception.Which.StatusCode.Should().Be(403);
        exception.Which.Code.Should().Be("cannot_change_admin_role");
    }

    [Test]
    public async Task UpdateRoleAsync_CannotRevokeAdminRole()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var admin1 = SeedUser(db, roleId: 1, "Admin One");
        var admin2 = SeedUser(db, roleId: 1, "Admin Two");
        var sut = new AdminUserService(db, new AuditLogService(db), StubGoTrue);

        var act = () => sut.UpdateRoleAsync(
            admin1.SupabaseUserId,
            admin2.Id,
            Role.StudentRoleName,
            null,
            null);

        var exception = await act.Should().ThrowAsync<AdminException>();
        exception.Which.StatusCode.Should().Be(403);
        exception.Which.Code.Should().Be("cannot_change_admin_role");
    }

    [Test]
    public async Task UpdateRoleAsync_NonAdminActor_Throws403()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var actor = SeedUser(db, roleId: 2, "Student Actor");
        var target = SeedUser(db, roleId: 2, "Student Target");
        var sut = new AdminUserService(db, new AuditLogService(db), StubGoTrue);

        var act = () => sut.UpdateRoleAsync(
            actor.SupabaseUserId,
            target.Id,
            Role.ModeratorRoleName,
            null,
            null);

        var exception = await act.Should().ThrowAsync<AdminException>();
        exception.Which.StatusCode.Should().Be(403);
        exception.Which.Code.Should().Be("admin_required");
    }

    private static readonly IGoTrueClient StubGoTrue = new StubGoTrueClient();

    private sealed class StubGoTrueClient : IGoTrueClient
    {
        public Task<GoTrueUser> AdminUpdateUserByIdAsync(Guid userId, Dictionary<string, object?>? appMetadata, CancellationToken cancellationToken = default)
            => Task.FromResult(new GoTrueUser
            {
                Id = userId,
                Email = "stub@test.com",
                AppMetadata = appMetadata,
            });

        public Task AdminSignOutUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AdminDeleteUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        // Unused stubs
        public Task<GoTrueSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GoTrueSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SignOutAsync(string accessToken, bool global, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GoTrueUser> GetUserAsync(string accessToken, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GoTrueUser> UpdateUserAsync(string accessToken, string? email, string? password, Dictionary<string, object?>? metadata, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GoTrueUser> AdminCreateUserAsync(string email, string password, Dictionary<string, object?>? userMetadata, Dictionary<string, object?>? appMetadata, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GoTrueUser?> AdminGetUserByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static User SeedUser(Data.AppDbContext db, int roleId, string fullName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..12],
            FullName = fullName,
            IsActive = true,
            DailyTokenQuota = 25_000,
            TokenUsageDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }
}
