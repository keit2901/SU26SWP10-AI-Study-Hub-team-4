using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services.Payment;
using AI_Study_Hub_v2.Services.Payment.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Background service that periodically scans for expired UserPlans and moves them
/// to the Free plan if no other active plan exists. Also expires stale VNPay payments.
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

        // Initial short delay to let app fully start, then immediate first scan
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpirePlansAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — let the Task.Delay below propagate cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PlanExpiryHostedService encountered an error during expiry scan.");
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }

        _logger.LogInformation("PlanExpiryHostedService stopped.");
    }

    private async Task ExpirePlansAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var planService = scope.ServiceProvider.GetRequiredService<IPlanService>();
        var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

        var now = DateTimeOffset.UtcNow;

        // Expire stale pending payments
        var expiredPayments = await paymentService.ExpireStalePaymentsAsync(ct);
        if (expiredPayments > 0)
        {
            _logger.LogInformation("Expired {Count} stale pending payments.", expiredPayments);
        }

        // M4: Atomic bulk update to prevent overwriting admin "deactivated"
        var expiredCount = await db.UserPlans
            .Where(up => up.Status == "active" && up.ExpiresAt != null && up.ExpiresAt < now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(up => up.Status, "expired"),
                ct);

        if (expiredCount == 0)
        {
            // B4: compensatory scan — catch users with status='expired' but no active plan
            await AssignFreeToExpiredOrphansAsync(db, planService, now, ct);
            return;
        }

        _logger.LogInformation("Found {Count} expired UserPlans to process.", expiredCount);

        // M4: Query affected user IDs after atomic update
        var affectedUserIds = await db.UserPlans
            .Where(up => up.Status == "expired" && up.ExpiresAt < now)
            .Select(up => up.UserId)
            .Distinct()
            .ToListAsync(ct);

        // Assign Free plan to users who no longer have any active plan
        var freePlan = planService.GetFreePlan();
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

        // B4: single SaveChanges — Free assignment atomically
        await db.SaveChangesAsync(ct);

        // B4: compensatory scan for previously broken state
        await AssignFreeToExpiredOrphansAsync(db, planService, now, ct);

        _logger.LogInformation("Plan expiry scan complete. Expired {Count} plans, assigned Free plan to affected users.",
            expiredCount);
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
        // Only scan plans expired in the last 90 days to prevent unbounded growth
        var cutoff = now.AddDays(-90);
        var orphanUserIds = await db.UserPlans
            .Where(up => up.Status == "expired" && up.ExpiresAt >= cutoff)
            .Select(up => up.UserId)
            .Distinct()
            .ToListAsync(ct);

        // Batch check: get all users with active plans in one query
        var usersWithActivePlans = await db.UserPlans
            .Where(up => orphanUserIds.Contains(up.UserId) && up.Status == "active")
            .Select(up => up.UserId)
            .Distinct()
            .ToListAsync(ct);

        var usersNeedingFreePlan = orphanUserIds.Except(usersWithActivePlans).ToList();

        var freePlan = planService.GetFreePlan();
        var fixedCount = 0;
        foreach (var userId in usersNeedingFreePlan)
        {
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

        if (fixedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Compensatory scan: assigned Free plan to {Count} orphaned users.", fixedCount);
        }
    }
}
