using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class AiQuotaServiceTests
{
    [Test]
    public async Task ReserveAndCompleteAsync_ReconcilesDailyAndLifetimeUsage()
    {
        await using var db = TestDb.CreateInMemory();
        var user = SeedUser(db, dailyQuota: 10_000, usedToday: 100);
        var sut = new AiQuotaService(db, new AuditLogService(db));

        var reservation = await sut.ReserveAsync(user.SupabaseUserId, 600);
        (await db.Users.SingleAsync(item => item.Id == user.Id))
            .TokensUsedToday.Should().Be(700);

        await sut.CompleteAsync(reservation, 450);

        var updated = await db.Users.SingleAsync(item => item.Id == user.Id);
        updated.TokensUsedToday.Should().Be(550);
        updated.TotalTokensUsed.Should().Be(450);
    }

    [Test]
    public async Task ReserveAsync_InsufficientRemainingQuota_Throws429AndAuditsBlock()
    {
        await using var db = TestDb.CreateInMemory();
        var user = SeedUser(db, dailyQuota: 1_000, usedToday: 950);
        var sut = new AiQuotaService(db, new AuditLogService(db));

        var act = () => sut.ReserveAsync(user.SupabaseUserId, 100);

        var exception = await act.Should().ThrowAsync<AiQuotaException>();
        exception.Which.StatusCode.Should().Be(429);
        exception.Which.Code.Should().Be("ai_quota_exceeded");
        db.AuditLogs.Should().ContainSingle(log =>
            log.ActorUserId == user.Id && log.Action == "AI_QUOTA_BLOCKED");
        (await db.Users.SingleAsync(item => item.Id == user.Id))
            .TokensUsedToday.Should().Be(950);
    }

    [Test]
    public async Task ReleaseAsync_FailedRequest_ReturnsReservedTokens()
    {
        await using var db = TestDb.CreateInMemory();
        var user = SeedUser(db, dailyQuota: 5_000, usedToday: 300);
        var sut = new AiQuotaService(db, new AuditLogService(db));

        var reservation = await sut.ReserveAsync(user.SupabaseUserId, 500);
        await sut.ReleaseAsync(reservation);

        (await db.Users.SingleAsync(item => item.Id == user.Id))
            .TokensUsedToday.Should().Be(300);
    }

    private static User SeedUser(
        Data.AppDbContext db,
        long dailyQuota,
        long usedToday)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..12],
            FullName = "Quota Student",
            IsActive = true,
            DailyTokenQuota = dailyQuota,
            TokensUsedToday = usedToday,
            TokenUsageDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }
}
