using System.Security.Claims;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Controllers;

/// <summary>
/// Plan catalog and current-user quota endpoints. All endpoints require a valid Bearer JWT
/// issued by Supabase GoTrue.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PlansController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly IStorageQuotaService _quotaService;
    private readonly AppDbContext _db;
    private readonly ILogger<PlansController> _logger;

    public PlansController(
        IPlanService planService,
        IStorageQuotaService quotaService,
        AppDbContext db,
        ILogger<PlansController> logger)
    {
        _planService = planService;
        _quotaService = quotaService;
        _db = db;
        _logger = logger;
    }

    /// <summary>Returns all active plans, ordered by SortOrder.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public IActionResult GetPlans()
    {
        var plans = _planService.GetActivePlans()
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
            .ToList();
        return Ok(plans);
    }

    /// <summary>Returns the calling user's plan and current storage usage vs quota.</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(StorageQuotaSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentPlan(CancellationToken ct)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();
            var snapshot = await _quotaService.GetSnapshotAsync(supabaseUserId, ct);
            return Ok(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected plan snapshot failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while fetching the plan snapshot."
            });
        }
    }

    /// <summary>Self-service plan purchase / upgrade for the authenticated user.</summary>
    [HttpPost("purchase")]
    [ProducesResponseType(typeof(UserPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PurchasePlan(
        [FromBody] PurchasePlanRequest request,
        CancellationToken ct)
    {
        try
        {
            var supabaseUserId = GetSupabaseUserIdFromClaims();

            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct);
            if (user is null)
            {
                return NotFound(new ApiErrorResponse
                {
                    Code = "user_not_found",
                    Message = "User not found."
                });
            }

            var plan = _planService.GetPlanByKey(request.PlanKey);
            if (plan is null)
            {
                return NotFound(new ApiErrorResponse
                {
                    Code = "plan_not_found",
                    Message = $"Plan '{request.PlanKey}' not found."
                });
            }

            var now = DateTimeOffset.UtcNow;

            // Deactivate all existing active plan assignments for this user
            var existingActivePlans = await _db.UserPlans
                .Where(up => up.UserId == user.Id && up.Status == "active")
                .ToListAsync(ct);

            foreach (var existingPlan in existingActivePlans)
            {
                existingPlan.Status = "deactivated";
            }

            var newUserPlan = new UserPlan
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PlanId = plan.Id,
                Status = "active",
                AssignedAt = now,
                ExpiresAt = null,
                PaidAt = now,
            };
            _db.UserPlans.Add(newUserPlan);

            var amountVnd = request.BillingCycle == "yearly"
                ? (plan.YearlyPriceVnd ?? 0)
                : (plan.MonthlyPriceVnd ?? 0);

            var txnRef = $"demo_{Guid.NewGuid():N}"[..20];

            var paymentTransaction = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                UserPlanId = newUserPlan.Id,
                TxnRef = txnRef,
                PlanKey = request.PlanKey,
                BillingCycle = request.BillingCycle,
                AmountVnd = amountVnd,
                Status = "demo_completed",
                CreatedAt = now,
                CompletedAt = now,
            };
            _db.PaymentTransactions.Add(paymentTransaction);

            await _db.SaveChangesAsync(ct);

            var snapshot = await _quotaService.GetSnapshotAsync(supabaseUserId, ct);

            _logger.LogInformation(
                "User {UserId} purchased plan {PlanKey} ({BillingCycle}) — UserPlan {UserPlanId}",
                user.Id, plan.PlanKey, request.BillingCycle, newUserPlan.Id);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected plan purchase failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while processing the purchase."
            });
        }
    }

    private Guid GetSupabaseUserIdFromClaims()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (Guid.TryParse(sub, out var id))
        {
            return id;
        }

        throw new InvalidOperationException("Authenticated Supabase user id is missing or invalid.");
    }
}
