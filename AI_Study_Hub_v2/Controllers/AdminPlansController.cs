using System.Security.Claims;
using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Controllers;

/// <summary>
/// Admin-only plan management, user plan assignment, and storage reconciliation endpoints.
/// </summary>
[ApiController]
[Route("api/admin/plans")]
[Produces("application/json")]
[EnableRateLimiting("admin")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class AdminPlansController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPlanService _planService;
    private readonly IStorageQuotaService _quotaService;
    private readonly IStorageReconciliationService _reconciliationService;
    private readonly IAuditLogService _audit;
    private readonly ILogger<AdminPlansController> _logger;

    public AdminPlansController(
        AppDbContext db,
        IPlanService planService,
        IStorageQuotaService quotaService,
        IStorageReconciliationService reconciliationService,
        IAuditLogService audit,
        ILogger<AdminPlansController> logger)
    {
        _db = db;
        _planService = planService;
        _quotaService = quotaService;
        _reconciliationService = reconciliationService;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>Returns all plans (including inactive).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllPlans(CancellationToken ct)
    {
        var plans = await _db.Plans
            .OrderBy(p => p.SortOrder)
            .Select(p => new PlanDto(
                p.PlanKey,
                p.DisplayName,
                p.Description,
                p.StorageQuotaBytes,
                p.MaxDocumentCount,
                p.MaxFolderCount,
                p.DailyTokenQuota,
                p.MaxFileSizeBytes,
                p.MaxDocsPerFolder,
                p.MonthlyPriceVnd,
                p.YearlyPriceVnd))
            .ToListAsync(ct);
        return Ok(plans);
    }

    /// <summary>Updates plan limits (storage, document count, token quota, etc.).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] UpdatePlanRequest request, CancellationToken ct)
    {
        var plan = await _db.Plans.FindAsync([id], ct);
        if (plan is null) return NotFound(new ApiErrorResponse { Code = "plan_not_found", Message = "Plan not found." });

        // H2: Prevent reducing quota below active users' usage
        if (request.StorageQuotaBytes.HasValue && plan.StorageQuotaBytes > request.StorageQuotaBytes.Value)
        {
            var overQuotaUsers = await _db.UserPlans
                .Where(up => up.PlanId == id && up.Status == "active")
                .Join(_db.Users, up => up.UserId, u => u.Id, (up, u) => u)
                .Where(u => u.StorageUsedBytes > request.StorageQuotaBytes.Value)
                .CountAsync(ct);

            if (overQuotaUsers > 0)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "quota_conflict",
                    Message = $"Cannot reduce storage quota: {overQuotaUsers} active users exceed the new limit."
                });
            }
        }

        if (request.MaxDocumentCount.HasValue && plan.MaxDocumentCount > request.MaxDocumentCount.Value)
        {
            var overQuotaUsers = await _db.UserPlans
                .Where(up => up.PlanId == id && up.Status == "active")
                .Select(up => new
                {
                    up.UserId,
                    DocumentCount = _db.Documents.Count(d => d.UserId == up.UserId)
                })
                .Where(x => x.DocumentCount > request.MaxDocumentCount.Value)
                .CountAsync(ct);

            if (overQuotaUsers > 0)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "quota_conflict",
                    Message = $"Cannot reduce document count limit: {overQuotaUsers} active users exceed the new limit."
                });
            }
        }

        if (request.MaxFolderCount.HasValue && plan.MaxFolderCount > request.MaxFolderCount.Value)
        {
            var overQuotaUsers = await _db.UserPlans
                .Where(up => up.PlanId == id && up.Status == "active")
                .Select(up => new
                {
                    up.UserId,
                    FolderCount = _db.Folders.Count(f => f.UserId == up.UserId)
                })
                .Where(x => x.FolderCount > request.MaxFolderCount.Value)
                .CountAsync(ct);

            if (overQuotaUsers > 0)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "quota_conflict",
                    Message = $"Cannot reduce folder count limit: {overQuotaUsers} active users exceed the new limit."
                });
            }
        }

        var beforeJson = JsonSerializer.Serialize(new
        {
            plan.StorageQuotaBytes,
            plan.MaxDocumentCount,
            plan.MaxFolderCount,
            plan.DailyTokenQuota,
            plan.MaxFileSizeBytes,
            plan.MaxDocsPerFolder,
        });

        plan.StorageQuotaBytes = request.StorageQuotaBytes;
        plan.MaxDocumentCount = request.MaxDocumentCount;
        plan.MaxFolderCount = request.MaxFolderCount;
        plan.DailyTokenQuota = request.DailyTokenQuota;
        plan.MaxFileSizeBytes = request.MaxFileSizeBytes;
        plan.MaxDocsPerFolder = request.MaxDocsPerFolder;

        await _db.SaveChangesAsync(ct);

        // M5.6: invalidate the plan cache so users see updated values immediately
        _planService.InvalidateCache();

        var afterJson = JsonSerializer.Serialize(new
        {
            plan.StorageQuotaBytes,
            plan.MaxDocumentCount,
            plan.MaxFolderCount,
            plan.DailyTokenQuota,
            plan.MaxFileSizeBytes,
            plan.MaxDocsPerFolder,
        });

        _audit.Add(
            GetSupabaseUserId(),
            "UpdatePlan",
            "Plan",
            plan.Id.ToString(),
            severity: "Medium",
            beforeJson: beforeJson,
            afterJson: afterJson,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            requestId: HttpContext.TraceIdentifier);

        _logger.LogInformation("Admin {AdminId} updated plan {PlanKey} ({PlanId}).",
            GetSupabaseUserId(), plan.PlanKey, plan.Id);

        return Ok(new PlanDto(
            plan.PlanKey,
            plan.DisplayName,
            plan.Description,
            plan.StorageQuotaBytes,
            plan.MaxDocumentCount,
            plan.MaxFolderCount,
            plan.DailyTokenQuota,
            plan.MaxFileSizeBytes,
            plan.MaxDocsPerFolder,
            plan.MonthlyPriceVnd,
            plan.YearlyPriceVnd));
    }

    /// <summary>Returns the active plan assigned to a specific user.</summary>
    [HttpGet("users/{userId:guid}/plan")]
    [ProducesResponseType(typeof(UserPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserPlan(Guid userId, CancellationToken ct)
    {
        var userPlan = await _db.UserPlans
            .Include(up => up.Plan)
            .Include(up => up.User)
            .Where(up => up.UserId == userId && up.Status == "active")
            .OrderByDescending(up => up.AssignedAt)
            .FirstOrDefaultAsync(ct);

        if (userPlan is null)
        {
            return NotFound(new ApiErrorResponse { Code = "user_plan_not_found", Message = "No active plan found for this user." });
        }

        var snapshot = await _quotaService.GetSnapshotAsync(
            userPlan.User.SupabaseUserId, ct);

        return Ok(new UserPlanDto(
            userPlan.Id,
            userPlan.PlanId,
            userPlan.Plan.PlanKey,
            userPlan.Plan.DisplayName,
            userPlan.Status,
            userPlan.AssignedAt,
            userPlan.ExpiresAt,
            userPlan.PaidAt,
            snapshot));
    }

    /// <summary>Manually assigns a plan to a user (deactivates existing active plans).</summary>
    [HttpPatch("users/{userId:guid}/plan")]
    [ProducesResponseType(typeof(UserPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignPlan(Guid userId, [FromBody] AssignPlanRequest request, CancellationToken ct)
    {
        var plan = _planService.GetPlanByKey(request.PlanKey);
        if (plan is null)
        {
            return NotFound(new ApiErrorResponse { Code = "plan_not_found", Message = "The requested plan was not found." });
        }

        // Verify the user exists
        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return NotFound(new ApiErrorResponse { Code = "user_not_found", Message = "User not found." });
        }

        // Deactivate all existing active plan assignments for this user
        var existingActivePlans = await _db.UserPlans
            .Where(up => up.UserId == userId && up.Status == "active")
            .ToListAsync(ct);

        foreach (var existingPlan in existingActivePlans)
        {
            existingPlan.Status = "deactivated";
        }

        // Create the new UserPlan assignment
        var newUserPlan = new UserPlan
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanId = plan.Id,
            Status = "active",
            AssignedAt = DateTimeOffset.UtcNow,
            ExpiresAt = null, // Manual assignment; no expiry unless specified
            PaidAt = null,
        };
        _db.UserPlans.Add(newUserPlan);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "plan_assignment_failed",
                Message = "Could not assign the plan. The plan may have been deleted or the user already has an active plan."
            });
        }

        _audit.Add(
            GetSupabaseUserId(),
            "ManualPlanAssignment",
            "UserPlan",
            newUserPlan.Id.ToString(),
            severity: "Medium",
            beforeJson: existingActivePlans.Count > 0
                ? JsonSerializer.Serialize(existingActivePlans.Select(p => new { p.Id, p.PlanId, p.Status }))
                : null,
            afterJson: JsonSerializer.Serialize(new { newUserPlan.Id, newUserPlan.PlanId, newUserPlan.Status }),
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            requestId: HttpContext.TraceIdentifier);

        _logger.LogInformation("Admin {AdminId} assigned plan {PlanKey} to user {UserId}.",
            GetSupabaseUserId(), plan.PlanKey, userId);

        var snapshot = await _quotaService.GetSnapshotAsync(user.SupabaseUserId, ct);

        return Ok(new UserPlanDto(
            newUserPlan.Id,
            plan.Id,
            plan.PlanKey,
            plan.DisplayName,
            "active",
            newUserPlan.AssignedAt,
            newUserPlan.ExpiresAt,
            newUserPlan.PaidAt,
            snapshot));
    }

    /// <summary>Forces a full storage usage reconciliation across all users.</summary>
    [HttpPost("storage/reconcile")]
    [ProducesResponseType(typeof(IReadOnlyList<StorageDiscrepancy>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReconcileStorage(CancellationToken ct)
    {
        var discrepancies = await _reconciliationService.ReconcileAllAsync(ct);

        _audit.Add(
            GetSupabaseUserId(),
            "StorageReconciliation",
            "Storage",
            severity: "Low",
            contextJson: JsonSerializer.Serialize(new { discrepancyCount = discrepancies.Count }),
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            requestId: HttpContext.TraceIdentifier);

        _logger.LogInformation("Admin {AdminId} triggered storage reconciliation. {Count} discrepancies found.",
            GetSupabaseUserId(), discrepancies.Count);

        return Ok(discrepancies);
    }

    /// <summary>Returns recent payment transactions for admin review.</summary>
    [HttpGet("payments")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentTransactionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100;

        var totalCount = await _db.PaymentTransactions.CountAsync(ct);

        var payments = await _db.PaymentTransactions
            .Include(pt => pt.User)
            .OrderByDescending(pt => pt.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(pt => new PaymentTransactionDto(
                pt.Id,
                pt.UserId,
                pt.User.Username,
                pt.PlanKey,
                pt.BillingCycle,
                pt.AmountVnd,
                pt.Status,
                pt.CreatedAt,
                pt.CompletedAt,
                pt.ExpiresAt,
                pt.ErrorMessage))
            .ToListAsync(ct);

        return Ok(new
        {
            data = payments,
            page,
            pageSize,
            totalCount
        });
    }

    private Guid GetSupabaseUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(value, out var id))
        {
            return id;
        }
        throw new InvalidOperationException("Authenticated user id is missing or invalid.");
    }
}
