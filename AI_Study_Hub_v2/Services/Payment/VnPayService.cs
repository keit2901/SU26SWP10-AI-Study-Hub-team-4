using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Payment;

public sealed class VnPayService : IVnPayService
{
    private readonly AppDbContext _db;
    private readonly VnPaySettings _settings;
    private readonly IPlanService _planService;
    private readonly IAuditLogService _audit;
    private readonly ILogger<VnPayService> _logger;

    public VnPayService(
        AppDbContext db,
        IOptions<VnPaySettings> options,
        IPlanService planService,
        IAuditLogService audit,
        ILogger<VnPayService> logger)
    {
        _db = db;
        _settings = options.Value;
        _planService = planService;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Dtos.PaymentUrlResponse> CreatePaymentAsync(
        Guid userId, string planKey, string billingCycle,
        string userIpAddress, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            throw new InvalidOperationException("User not found");

        var plan = _planService.GetPlanByKey(planKey);
        if (plan is null)
            throw new InvalidOperationException("Plan not found");

        // FC-04: Check for existing pending payment
        var existingPending = await _db.PaymentTransactions
            .FirstOrDefaultAsync(pt => pt.UserId == userId && pt.Status == "pending", ct);
        if (existingPending is not null)
        {
            var expiresAt = existingPending.ExpiresAt ?? existingPending.CreatedAt.AddMinutes(_settings.ExpireMinutes);
            existingPending.ExpiresAt ??= expiresAt;

            if (expiresAt > now)
            {
                if (string.Equals(existingPending.PlanKey, planKey, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existingPending.BillingCycle, billingCycle, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildPaymentResponse(existingPending, userIpAddress);
                }

                existingPending.Status = "expired";
                existingPending.ErrorMessage = "Payment superseded by a new purchase attempt.";
                existingPending.CompletedAt = now;
            }
            else
            {
                existingPending.Status = "expired";
                existingPending.ErrorMessage = "Payment expired before new purchase.";
                existingPending.CompletedAt = now;
            }

            await _db.SaveChangesAsync(ct);
        }

        var amountVnd = billingCycle == "yearly"
            ? (plan.YearlyPriceVnd ?? 0)
            : (plan.MonthlyPriceVnd ?? 0);

        if (amountVnd <= 0)
            throw new InvalidOperationException("This plan has no price configured.");

        var txnRef = $"VP_{Guid.NewGuid():N}"[..20];
        var orderInfo = $"AI Study Hub - {plan.PlanKey} {billingCycle}";

        var txn = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserPlanId = null,
            TxnRef = txnRef,
            PlanKey = planKey,
            BillingCycle = billingCycle,
            AmountVnd = amountVnd,
            Status = "pending",
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_settings.ExpireMinutes),
        };
        _db.PaymentTransactions.Add(txn);
        await _db.SaveChangesAsync(ct);

        return BuildPaymentResponse(txn, userIpAddress);
    }

    public async Task<Dtos.IpnResult> ProcessIpnCallbackAsync(
        IQueryCollection queryParams, CancellationToken ct)
    {
        if (!VnPayLibrary.VerifyHash(queryParams, _settings.HashSecret))
            return new Dtos.IpnResult("97", "Invalid signature");

        var parsed = VnPayLibrary.ParseQueryParams(queryParams);

        var txnRef = parsed.GetValueOrDefault("vnp_TxnRef", "");
        parsed.TryGetValue("vnp_ResponseCode", out var responseCode);
        parsed.TryGetValue("vnp_TransactionStatus", out var transactionStatus);
        parsed.TryGetValue("vnp_BankCode", out var bankCode);
        parsed.TryGetValue("vnp_PayDate", out var payDate);

        var txn = await _db.PaymentTransactions
            .Include(pt => pt.User)
            .FirstOrDefaultAsync(pt => pt.TxnRef == txnRef, ct);
        if (txn is null)
            return new Dtos.IpnResult("01", "Order not found");

        // Idempotency: already processed
        if (txn.Status != "pending")
            return new Dtos.IpnResult("02", "Order already updated");

        var responseJson = JsonSerializer.Serialize(parsed);

        if (responseCode == "00" && transactionStatus == "00")
        {
            // SUCCESS
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var user = await _db.Users.FindAsync(new object[] { txn.UserId }, ct);
            if (user is null)
            {
                txn.Status = "failed";
                txn.ErrorMessage = "User not found during IPN processing.";
                txn.VnpayResponseJson = responseJson;
                await _db.SaveChangesAsync(ct);
                return new Dtos.IpnResult("01", "User not found");
            }

            var plan = _planService.GetPlanByKey(txn.PlanKey);
            if (plan is null)
            {
                txn.Status = "failed";
                txn.ErrorMessage = "Plan not found during IPN processing.";
                txn.VnpayResponseJson = responseJson;
                await _db.SaveChangesAsync(ct);
                return new Dtos.IpnResult("01", "Plan not found");
            }

            // Deactivate existing active UserPlans
            var activePlans = await _db.UserPlans
                .Where(up => up.UserId == txn.UserId && up.Status == "active")
                .ToListAsync(ct);
            foreach (var ap in activePlans) ap.Status = "deactivated";

            // Calculate ExpiresAt
            DateTimeOffset? expiresAt = txn.BillingCycle switch
            {
                "yearly" => DateTimeOffset.UtcNow.AddYears(1),
                _ => DateTimeOffset.UtcNow.AddMonths(1),
            };
            if (plan.PlanKey == "free") expiresAt = null;

            var userPlan = new UserPlan
            {
                Id = Guid.NewGuid(),
                UserId = txn.UserId,
                PlanId = plan.Id,
                Status = "active",
                AssignedAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiresAt,
                PaidAt = DateTimeOffset.UtcNow,
            };
            _db.UserPlans.Add(userPlan);

            txn.Status = "completed";
            txn.CompletedAt = DateTimeOffset.UtcNow;
            txn.UserPlanId = userPlan.Id;
            txn.VnpayResponseJson = responseJson;

            await _db.SaveChangesAsync(ct);

            // Audit log
            try
            {
                _audit.Add(
                    null, // ActorUserId — this is server-side IPN, no calling user context
                    "PlanPaymentCompleted",
                    "UserPlan",
                    userPlan.Id.ToString(),
                    severity: "Medium",
                    contextJson: JsonSerializer.Serialize(new
                    {
                        paymentTransactionId = txn.Id,
                        status = txn.Status,
                        outcome = "completed",
                    }),
                    ipAddress: null,
                    requestId: null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log audit event for payment completion");
            }

            await tx.CommitAsync(ct);
            return new Dtos.IpnResult("00", "Confirm Success");
        }
        else
        {
            // FAILED
            txn.Status = "failed";
            txn.ErrorMessage = $"VNPay response: {responseCode}";
            txn.VnpayResponseJson = responseJson;
            await _db.SaveChangesAsync(ct);
            return new Dtos.IpnResult("00", "Confirm Success");
        }
    }

    public async Task<Dtos.ReturnUrlResult> VerifyReturnUrlAsync(
        IQueryCollection queryParams, CancellationToken ct)
    {
        if (!VnPayLibrary.VerifyHash(queryParams, _settings.HashSecret))
            return new Dtos.ReturnUrlResult(false, "invalid", null, 0, "Invalid signature");

        var parsed = VnPayLibrary.ParseQueryParams(queryParams);
        var txnRef = parsed.GetValueOrDefault("vnp_TxnRef", "");

        var txn = await _db.PaymentTransactions
            .FirstOrDefaultAsync(pt => pt.TxnRef == txnRef, ct);

        if (txn is null)
            return new Dtos.ReturnUrlResult(true, "unknown", null, 0, "Transaction not found.");

        var plan = _planService.GetPlanByKey(txn.PlanKey);
        return new Dtos.ReturnUrlResult(true, txn.Status, plan?.DisplayName, txn.AmountVnd, txn.ErrorMessage);
    }

    public async Task<int> ExpireStalePaymentsAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_settings.ExpireMinutes);
        var expired = await _db.PaymentTransactions
            .Where(pt => pt.Status == "pending" && pt.CreatedAt < cutoff)
            .ToListAsync(ct);

        int count = 0;
        foreach (var txn in expired)
        {
            txn.Status = "expired";
            txn.ErrorMessage = "Payment expired after 15 minutes.";
            count++;
        }
        if (count > 0) await _db.SaveChangesAsync(ct);
        return count;
    }

    private Dtos.PaymentUrlResponse BuildPaymentResponse(
        PaymentTransaction txn,
        string userIpAddress)
    {
        var createdAt = txn.CreatedAt == default ? DateTimeOffset.UtcNow : txn.CreatedAt;
        var expiresAt = txn.ExpiresAt ?? createdAt.AddMinutes(_settings.ExpireMinutes);
        var orderInfo = $"AI Study Hub - {txn.PlanKey} {txn.BillingCycle}";
        var paymentUrl = VnPayLibrary.BuildPaymentUrl(
            _settings,
            txn.TxnRef,
            txn.AmountVnd,
            orderInfo,
            userIpAddress,
            createdAt);

        return new Dtos.PaymentUrlResponse(
            paymentUrl,
            txn.TxnRef,
            txn.PlanKey,
            txn.BillingCycle,
            txn.AmountVnd,
            expiresAt);
    }
}
