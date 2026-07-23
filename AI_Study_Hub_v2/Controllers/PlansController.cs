using System.Security.Claims;
using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Payment;
using AI_Study_Hub_v2.Services.Payment.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly IPaymentService _paymentService;
    private readonly AppDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly ILogger<PlansController> _logger;

    public PlansController(
        IPlanService planService,
        IStorageQuotaService quotaService,
        IPaymentService paymentService,
        AppDbContext db,
        IAuditLogService audit,
        ILogger<PlansController> logger)
    {
        _planService = planService;
        _quotaService = quotaService;
        _paymentService = paymentService;
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>Returns all active plans, ordered by SortOrder.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<PlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public IActionResult GetPlans()
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch plans.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while fetching plans."
            });
        }
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

    /// <summary>Returns recent payment transactions for the authenticated user.</summary>
    [HttpGet("payments")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentTransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyPaymentTransactions(
        [FromQuery] int take = 12,
        CancellationToken ct = default)
    {
        try
        {
            if (take < 1) take = 12;
            if (take > 50) take = 50;

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

            var payments = await _db.PaymentTransactions
                .Where(pt => pt.UserId == user.Id)
                .OrderByDescending(pt => pt.CreatedAt)
                .Take(take)
                .Select(pt => new PaymentTransactionDto(
                    pt.Id,
                    pt.UserId,
                    user.Username,
                    pt.PlanKey,
                    pt.BillingCycle,
                    pt.AmountVnd,
                    pt.Status,
                    pt.CreatedAt,
                    pt.CompletedAt,
                    pt.ExpiresAt,
                    pt.ErrorMessage))
                 .ToListAsync(ct);

            return Ok(payments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected payment history fetch failure.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "unexpected_error",
                Message = "An unexpected error occurred while fetching payment history."
            });
        }
    }

    /// <summary>Self-service plan purchase / upgrade for the authenticated user.</summary>
    [HttpPost("purchase")]
    [EnableRateLimiting("purchase")]
    [ProducesResponseType(typeof(UserPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaymentUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
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

            // M5.2: idempotency check
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existingTxn = await _db.PaymentTransactions
                    .FirstOrDefaultAsync(pt => pt.TxnRef == request.IdempotencyKey && pt.UserId == user.Id, ct);
                if (existingTxn is not null)
                {
                    if (existingTxn.UserPlanId is null)
                    {
                        return Conflict(new ApiErrorResponse
                        {
                            Code = "incomplete_transaction",
                            Message = "A previous transaction was not completed. Please contact support."
                        });
                    }

                    var existingUserPlan = await _db.UserPlans
                        .Include(up => up.Plan)
                        .FirstOrDefaultAsync(up => up.Id == existingTxn.UserPlanId, ct);
                    if (existingUserPlan is not null)
                    {
                        var existingSnapshot = await _quotaService.GetSnapshotAsync(supabaseUserId, ct);
                        return Ok(new UserPlanDto(
                            existingUserPlan.Id,
                            existingUserPlan.PlanId,
                            existingUserPlan.Plan.PlanKey,
                            existingUserPlan.Plan.DisplayName,
                            existingUserPlan.Status,
                            existingUserPlan.AssignedAt,
                            existingUserPlan.ExpiresAt,
                            existingUserPlan.PaidAt,
                            existingSnapshot));
                    }
                }
            }

            var plan = _planService.GetPlanByKey(request.PlanKey);
            if (plan is null)
            {
                // W3.1: don't echo user input in error messages
                return NotFound(new ApiErrorResponse
                {
                    Code = "plan_not_found",
                    Message = "The requested plan was not found."
                });
            }

            // H4: defense-in-depth — plan should already be active per service cache
            if (!plan.IsActive)
            {
                return NotFound(new ApiErrorResponse
                {
                    Code = "plan_not_found",
                    Message = "The requested plan was not found."
                });
            }

            // Validate billing cycle has a corresponding price
            if (request.BillingCycle == "yearly" && plan.YearlyPriceVnd is null)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "invalid_billing_cycle",
                    Message = "Yearly billing is not available for this plan."
                });
            }
            if (request.BillingCycle == "monthly" && plan.MonthlyPriceVnd is null)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "invalid_billing_cycle",
                    Message = "Monthly billing is not available for this plan."
                });
            }

            var now = DateTimeOffset.UtcNow;

            // F2.1: calculate ExpiresAt based on billing cycle
            DateTimeOffset? expiresAt = request.BillingCycle switch
            {
                "monthly" => now.AddMonths(1),
                "yearly" => now.AddYears(1),
                _ => now.AddMonths(1),
            };
            // Free plan never expires
            if (plan.PlanKey == "free")
            {
                expiresAt = null;
            }

            // BR-03 + FC-03 (Long review): block re-purchase of same plan + block downgrade
            var activePlanEntity = await _db.UserPlans
                .Where(up => up.UserId == user.Id && up.Status == "active")
                .Include(up => up.Plan)
                .FirstOrDefaultAsync(ct);

            if (activePlanEntity?.Plan is not null)
            {
                // BR-03: cannot re-purchase the same plan
                if (activePlanEntity.Plan.PlanKey == plan.PlanKey)
                {
                    return Conflict(new ApiErrorResponse
                    {
                        Code = "already_on_plan",
                        Message = "You are already on this plan."
                    });
                }

                // FC-03: upgrade only — block downgrade
                if (plan.SortOrder <= activePlanEntity.Plan.SortOrder)
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Code = "downgrade_not_allowed",
                        Message = "Downgrading is not supported. You can only upgrade to a higher plan."
                    });
                }
            }

            // Free plan: immediate activation (no VNPay)
            if (plan.PlanKey == "free")
            {
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
                    ExpiresAt = null, // Free plan never expires
                    PaidAt = now,
                };
                _db.UserPlans.Add(newUserPlan);

                // M5.2: use idempotency key as txnRef if provided
                var txnRef = request.IdempotencyKey ?? $"free_{Guid.NewGuid():N}"[..20];

                var paymentTransaction = new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    UserPlanId = newUserPlan.Id,
                    TxnRef = txnRef,
                    PlanKey = request.PlanKey,
                    BillingCycle = request.BillingCycle,
                    AmountVnd = 0,
                    Status = "completed",
                    CreatedAt = now,
                    CompletedAt = now,
                };
                _db.PaymentTransactions.Add(paymentTransaction);

                await _db.SaveChangesAsync(ct);

                // F5.1: audit logging on self-service purchases
                try
                {
                    _audit.Add(
                        supabaseUserId,
                        "SelfServicePlanPurchase",
                        "UserPlan",
                        newUserPlan.Id.ToString(),
                        severity: "Medium",
                        contextJson: JsonSerializer.Serialize(new
                        {
                            planKey = plan.PlanKey,
                            billingCycle = request.BillingCycle,
                            amountVnd = 0,
                        }),
                        ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                        requestId: HttpContext.TraceIdentifier);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to log audit event for plan purchase by user {UserId}", user.Id);
                }

                var snapshot = await _quotaService.GetSnapshotAsync(supabaseUserId, ct);

                _logger.LogInformation(
                    "User {UserId} purchased free plan — UserPlan {UserPlanId}",
                    user.Id, newUserPlan.Id);

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

            // Paid plan: create pending transaction + return VNPay URL
            var amountVnd = request.BillingCycle == "yearly"
                ? (plan.YearlyPriceVnd ?? 0)
                : (plan.MonthlyPriceVnd ?? 0);

            if (amountVnd <= 0)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Code = "invalid_price",
                    Message = "This plan has no price configured."
                });
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            try
            {
                var paymentResult = await _paymentService.CreatePaymentAsync(
                    user.Id, request.PlanKey, request.BillingCycle, ct);

                // Audit log for payment initiation
                try
                {
                    _audit.Add(
                        supabaseUserId,
                        "PlanPaymentInitiated",
                        "PaymentTransaction",
                        plan.Id.ToString(),
                        severity: "Low",
                        contextJson: JsonSerializer.Serialize(new
                        {
                            planKey = request.PlanKey,
                            billingCycle = request.BillingCycle,
                            amountVnd = paymentResult.AmountVnd,
                        }),
                        ipAddress: ipAddress,
                        requestId: HttpContext.TraceIdentifier);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to log audit for payment initiation");
                }

                return Ok(paymentResult);
            }
            catch (PaymentProviderException ex)
            {
                _logger.LogWarning(ex, "Payment provider unavailable during purchase for user {UserId}", user.Id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse
                {
                    Code = "payment_provider_unavailable",
                    Message = ex.Message
                });
            }
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse
            {
                Code = "purchase_target_not_found",
                Message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "invalid_purchase_request",
                Message = ex.Message
            });
        }
        // F2.2: handle concurrent purchase race condition
        catch (DbUpdateException)
        {
            return Conflict(new ApiErrorResponse
            {
                Code = "concurrent_purchase",
                Message = "Another purchase is being processed. Please try again."
            });
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

    /// <summary>Returns the status of a payment transaction (no plan activation).</summary>
    [HttpGet("payment/status/{txnRef}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ReturnUrlResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentStatus(string txnRef, CancellationToken ct)
    {
        var result = await _paymentService.VerifyReturnAsync(txnRef, ct);
        return Ok(result);
    }

    /// <summary>Marks a pending transaction as expired when user cancels on PayOS checkout.</summary>
    [HttpPost("payment/cancel/{txnRef}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelPayment(string txnRef, CancellationToken ct)
    {
        var cancelled = await _paymentService.CancelTransactionAsync(txnRef, ct);
        return Ok(new { cancelled });
    }

    /// <summary>Retry a failed or expired payment transaction.</summary>
    [HttpPost("purchase/retry/{txnRef}")]
    [EnableRateLimiting("purchase")]
    [ProducesResponseType(typeof(PaymentUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PurchaseRetry(string txnRef, CancellationToken ct)
    {
        var supabaseUserId = GetSupabaseUserIdFromClaims();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct);
        if (user is null) return NotFound(new ApiErrorResponse { Code = "user_not_found", Message = "User not found." });

        var oldTxn = await _db.PaymentTransactions
            .FirstOrDefaultAsync(pt => pt.TxnRef == txnRef && pt.UserId == user.Id, ct);
        if (oldTxn is null) return NotFound(new ApiErrorResponse { Code = "transaction_not_found", Message = "Transaction not found." });
        if (oldTxn.Status != "expired" && oldTxn.Status != "failed")
            return BadRequest(new ApiErrorResponse { Code = "transaction_not_retryable", Message = "Only expired or failed transactions can be retried." });

        var result = await _paymentService.CreatePaymentAsync(
            user.Id, oldTxn.PlanKey, oldTxn.BillingCycle, ct);
        return Ok(result);
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
