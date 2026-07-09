using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Background service that periodically scans for expired UserPlans and moves them
/// to the Free plan if no other active plan exists.
/// </summary>
public sealed class PlanExpiryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlanExpiryHostedService> _logger;
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(1);

    public PlanExpiryHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<PlanExpiryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PlanExpiryHostedService started. Scan interval: {Interval}", ScanInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ScanInterval, stoppingToken);
                await ExpirePlansAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PlanExpiryHostedService encountered an error during expiry scan.");
            }
        }

        _logger.LogInformation("PlanExpiryHostedService stopped.");
    }

    private async Task ExpirePlansAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var planService = scope.ServiceProvider.GetRequiredService<IPlanService>();

        var now = DateTimeOffset.UtcNow;

        var expiredPlans = await db.UserPlans
            .Where(up => up.Status == "active" && up.ExpiresAt != null && up.ExpiresAt < now)
            .ToListAsync(ct);

        if (expiredPlans.Count == 0)
        {
            // B4: compensatory scan — catch users with status='expired' but no active plan
            await AssignFreeToExpiredOrphansAsync(db, planService, now, ct);
            return;
        }

        _logger.LogInformation("Found {Count} expired UserPlans to process.", expiredPlans.Count);

        var freePlan = planService.GetFreePlan();

        // Mark expired plans
        foreach (var expiredPlan in expiredPlans)
        {
            expiredPlan.Status = "expired";
        }

        // Assign Free plan to users who no longer have any active plan
        var affectedUserIds = expiredPlans.Select(up => up.UserId).Distinct().ToList();
        foreach (var userId in affectedUserIds)
        {
            var hasActivePlan = await db.UserPlans
                .AnyAsync(up => up.UserId == userId && up.Status == "active", ct);

            if (!hasActivePlan)
            {
                var freeUserPlan = new UserPlan
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    PlanId = freePlan.Id,
                    Status = "active",
                    AssignedAt = now,
                    ExpiresAt = null, // Free plan never expires
                    PaidAt = null,
                };
                db.UserPlans.Add(freeUserPlan);
            }
        }

        // B4: single SaveChanges — both expiry and Free assignment atomically
        await db.SaveChangesAsync(ct);

        // B4: compensatory scan for previously broken state
        await AssignFreeToExpiredOrphansAsync(db, planService, now, ct);

        _logger.LogInformation("Plan expiry scan complete. Expired {Count} plans, assigned Free plan to affected users.",
            expiredPlans.Count);
    }

    /// <summary>
    /// B4: Compensatory scan — finds users whose plan status is "expired" but have no active plan,
    /// and assigns them the Free plan. This repairs state left broken by previous bugs.
    /// </summary>
    private async Task AssignFreeToExpiredOrphansAsync(
        AppDbContext db,
        IPlanService planService,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var orphanUserIds = await db.UserPlans
            .Where(up => up.Status == "expired")
            .Select(up => up.UserId)
            .Distinct()
            .ToListAsync(ct);

        var fixedCount = 0;
        foreach (var userId in orphanUserIds)
        {
            var hasActivePlan = await db.UserPlans
                .AnyAsync(up => up.UserId == userId && up.Status == "active", ct);

            if (!hasActivePlan)
            {
                var freePlan = planService.GetFreePlan();
                var freeUserPlan = new UserPlan
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    PlanId = freePlan.Id,
                    Status = "active",
                    AssignedAt = now,
                    ExpiresAt = null,
                    PaidAt = null,
                };
                db.UserPlans.Add(freeUserPlan);
                fixedCount++;
            }
        }

        if (fixedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Compensatory scan: assigned Free plan to {Count} orphaned users.", fixedCount);
        }
    }
}
