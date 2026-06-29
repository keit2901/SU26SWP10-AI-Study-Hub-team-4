using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
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
        var sut = new AdminUserService(db, new AuditLogService(db));

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
        var sut = new AdminUserService(db, new AuditLogService(db));

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
