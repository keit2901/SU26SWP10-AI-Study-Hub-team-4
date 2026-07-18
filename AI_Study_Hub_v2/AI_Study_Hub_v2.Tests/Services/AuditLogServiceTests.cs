using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class AuditLogServiceTests
{
    [Test]
    public void Add_PersistsAuditLogEntry()
    {
        using var db = TestDb.CreateInMemory();
        var sut = new AuditLogService(db);

        sut.Add(
            actorUserId: null,
            action: "LOGIN_FAILED",
            entityType: "User",
            entityId: "user-1",
            severity: "High",
            ipAddress: "127.0.0.1");
        db.SaveChanges();

        var log = db.AuditLogs.Single();
        log.Action.Should().Be("LOGIN_FAILED");
        log.EntityType.Should().Be("User");
        log.EntityId.Should().Be("user-1");
        log.Severity.Should().Be("High");
        log.IpAddress.Should().Be("127.0.0.1");
        log.Id.Should().NotBeEmpty();
    }

    [Test]
    public async Task ListAsync_ReturnsAllEntries_OrderedByCreatedAtDesc()
    {
        using var db = TestDb.CreateInMemory();
        var baseTime = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        db.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), Action = "FIRST", EntityType = "Test", CreatedAt = baseTime },
            new AuditLog { Id = Guid.NewGuid(), Action = "SECOND", EntityType = "Test", CreatedAt = baseTime.AddHours(1) },
            new AuditLog { Id = Guid.NewGuid(), Action = "THIRD", EntityType = "Test", CreatedAt = baseTime.AddHours(2) });
        db.SaveChanges();
        var sut = new AuditLogService(db);

        var result = await sut.ListAsync(action: null, from: null, to: null, limit: 50);

        result.Should().HaveCount(3);
        result[0].Action.Should().Be("THIRD");
        result[1].Action.Should().Be("SECOND");
        result[2].Action.Should().Be("FIRST");
    }

    [Test]
    public async Task ListAsync_FilterByAction_ReturnsOnlyMatching()
    {
        using var db = TestDb.CreateInMemory();
        var baseTime = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        db.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), Action = "ROLE_CHANGE", EntityType = "User", CreatedAt = baseTime },
            new AuditLog { Id = Guid.NewGuid(), Action = "QUOTA_UPDATE", EntityType = "User", CreatedAt = baseTime.AddHours(1) },
            new AuditLog { Id = Guid.NewGuid(), Action = "ROLE_CHANGE", EntityType = "User", CreatedAt = baseTime.AddHours(2) });
        db.SaveChanges();
        var sut = new AuditLogService(db);

        var result = await sut.ListAsync(action: "ROLE_CHANGE", from: null, to: null, limit: 50);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(dto => dto.Action.Should().Be("ROLE_CHANGE"));
    }

    [Test]
    public async Task ListAsync_RespectsLimit()
    {
        using var db = TestDb.CreateInMemory();
        var baseTime = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 10; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Action = $"LOG_{i:D2}",
                EntityType = "Test",
                CreatedAt = baseTime.AddHours(i)
            });
        }
        db.SaveChanges();
        var sut = new AuditLogService(db);

        var result = await sut.ListAsync(action: null, from: null, to: null, limit: 5);

        result.Should().HaveCount(5);
    }

    [Test]
    public async Task ListAsync_Empty_ReturnsEmptyList()
    {
        using var db = TestDb.CreateInMemory();
        var sut = new AuditLogService(db);

        var result = await sut.ListAsync(action: null, from: null, to: null, limit: 50);

        result.Should().BeEmpty();
    }

    [Test]
    public void Add_WithAllOptionalFields_PersistsThem()
    {
        using var db = TestDb.CreateInMemory();
        var admin = SeedUser(db, roleId: 1, "Admin User");
        var sut = new AuditLogService(db);

        sut.Add(
            actorUserId: admin.Id,
            action: "CONFIG_CHANGE",
            entityType: "SystemConfig",
            entityId: "ai.chat_model",
            severity: "Medium",
            beforeJson: "{\"value\":\"gpt-4\"}",
            afterJson: "{\"value\":\"gpt-4o\"}",
            contextJson: "{\"changed_by\":\"admin\"}",
            ipAddress: "192.168.1.100",
            requestId: "req-abc-123");
        db.SaveChanges();

        var log = db.AuditLogs.Single();
        log.ActorUserId.Should().Be(admin.Id);
        log.Action.Should().Be("CONFIG_CHANGE");
        log.EntityType.Should().Be("SystemConfig");
        log.EntityId.Should().Be("ai.chat_model");
        log.Severity.Should().Be("Medium");
        log.BeforeJson.Should().Be("{\"value\":\"gpt-4\"}");
        log.AfterJson.Should().Be("{\"value\":\"gpt-4o\"}");
        log.ContextJson.Should().Be("{\"changed_by\":\"admin\"}");
        log.IpAddress.Should().Be("192.168.1.100");
        log.RequestId.Should().Be("req-abc-123");
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
