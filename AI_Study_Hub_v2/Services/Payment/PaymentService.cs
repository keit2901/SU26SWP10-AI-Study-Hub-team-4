using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Payment.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Payment;

/// <summary>
/// Business logic layer for payment processing.
/// Orchestrates PaymentTransaction lifecycle: create, verify webhook, expire, status.
/// Delegates provider-specific operations to <see cref="IPaymentProvider"/>.
/// </summary>
public sealed class PaymentService : IPaymentService
{
    private readonly IPaymentProvider _provider;
    private readonly AppDbContext _db;
    private readonly IPlanService _planService;
    private readonly IAuditLogService _audit;
    private readonly PayOsSettings _settings;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentProvider provider,
        AppDbContext db,
        IPlanService planService,
        IAuditLogService audit,
        IOptions<PayOsSettings> options,
        ILogger<PaymentService> logger)
    {
        _provider = provider;
        _db = db;
        _planService = planService;
        _audit = audit;
        _settings = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new payment transaction and requests a payment link from the provider.
    /// </summary>
    public async Task<PaymentUrlResponse> CreatePaymentAsync(
        Guid userId, string planKey, string billingCycle, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            throw new KeyNotFoundException("User not found.");

        var plan = _planService.GetPlanByKey(planKey);
        if (plan is null)
            throw new KeyNotFoundException("Plan not found.");

        // FC-04: Expire any existing pending transactions so each purchase gets a fresh QR
        var existingPendings = await _db.PaymentTransactions
            .Where(pt => pt.UserId == userId && pt.Status == "pending")
            .ToListAsync(ct);
        foreach (var pending in existingPendings)
        {
            pending.Status = "expired";
            pending.ErrorMessage = "Superseded by a new purchase attempt.";
            pending.CompletedAt = now;
        }
        if (existingPendings.Count > 0) await _db.SaveChangesAsync(ct);

        var amountVnd = billingCycle == "yearly"
            ? (plan.YearlyPriceVnd ?? 0)
            : (plan.MonthlyPriceVnd ?? 0);

        if (amountVnd <= 0)
            throw new InvalidOperationException("This plan has no price configured.");

        // For PayOS, we use "PO_" + 9-digit order code as TxnRef
        var txnRef = PayOsProvider.GenerateTxnRef();
        var description = $"AI Study Hub - {plan.DisplayName} {billingCycle}";

        // Create payment transaction record (pending)
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

        // Call provider to create payment link
        var paymentRequest = new PaymentRequest(
            UserId: userId,
            TxnRef: txnRef,
            AmountVnd: amountVnd,
            Description: description,
            ReturnUrl: $"{ResolveBaseUrl()}/payment/result?status=success&txnRef={txnRef}",
            CancelUrl: $"{ResolveBaseUrl()}/payment/result?status=cancelled&txnRef={txnRef}");

        var result = await _provider.CreatePaymentLinkAsync(paymentRequest, ct);

        if (!result.Success)
        {
            txn.Status = "failed";
            txn.ErrorMessage = result.ErrorMessage;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Payment provider could not create link for user {UserId}, plan {PlanKey}, cycle {BillingCycle}: {ProviderMessage}",
                userId,
                planKey,
                billingCycle,
                result.ErrorMessage);
            throw new PaymentProviderException(
                "The payment gateway is temporarily unavailable. Please try again in a moment.",
                result.ErrorMessage);
        }

        // Store provider response in VnpayResponseJson (generic provider response storage)
        txn.VnpayResponseJson = JsonSerializer.Serialize(new
        {
            provider = _provider.ProviderName,
            providerTxnId = result.ProviderTxnId,
            paymentUrl = result.PaymentUrl,
        });
        await _db.SaveChangesAsync(ct);

        return new PaymentUrlResponse(
            result.PaymentUrl,
            txnRef,
            planKey,
            billingCycle,
            amountVnd,
            txn.ExpiresAt ?? now.AddMinutes(_settings.ExpireMinutes));
    }

    /// <summary>
    /// Processes an incoming webhook from the payment provider.
    /// Verifies authenticity, checks idempotency, and activates the plan on success.
    /// </summary>
    public async Task<WebhookResult> ProcessWebhookAsync(string rawBody, CancellationToken ct)
    {
        // Extract signature from webhook body (PayOS sends it as "signature" field)
        string signature;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            signature = doc.RootElement.TryGetProperty("signature", out var sigEl)
                ? sigEl.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return new WebhookResult(false, "Invalid body format.");
        }

        // Verify webhook via provider
        var verification = await _provider.VerifyWebhookAsync(rawBody, signature, ct);
        if (!verification.IsValid)
        {
            _logger.LogWarning("Webhook verification failed: {Error}", verification.ErrorMessage);
            return new WebhookResult(false, verification.ErrorMessage ?? "Verification failed.");
        }

        // Look up transaction by provider TxnId
        var txnRef = $"PO_{verification.ProviderTxnId}";
        var txn = await _db.PaymentTransactions
            .FirstOrDefaultAsync(pt => pt.TxnRef == txnRef, ct);

        if (txn is null)
        {
            _logger.LogWarning("Webhook for unknown transaction: ProviderTxnId={ProviderTxnId}", verification.ProviderTxnId);
            return new WebhookResult(false, "Transaction not found.");
        }

        // Idempotency: already processed
        if (txn.Status != "pending")
        {
            _logger.LogInformation("Webhook for already-processed transaction {TxnRef} (status={Status}) — idempotent skip.", txnRef, txn.Status);
            return new WebhookResult(true, "Already processed.");
        }

        // Verify amount matches
        if (verification.AmountVnd != txn.AmountVnd)
        {
            _logger.LogWarning("Webhook amount mismatch: expected {Expected}, got {Actual} for txn {TxnRef}",
                txn.AmountVnd, verification.AmountVnd, txnRef);
            txn.Status = "failed";
            txn.ErrorMessage = $"Amount mismatch: expected {txn.AmountVnd}, got {verification.AmountVnd}";
            txn.VnpayResponseJson = rawBody;
            await _db.SaveChangesAsync(ct);
            return new WebhookResult(false, "Amount mismatch.");
        }

        if (verification.Status == "PAID")
        {
            // SUCCESS: activate plan (shared with return-URL verification)
            var plan = _planService.GetPlanByKey(txn.PlanKey);
            if (plan is null)
            {
                txn.Status = "failed";
                txn.ErrorMessage = "Plan not found during webhook processing.";
                txn.VnpayResponseJson = rawBody;
                await _db.SaveChangesAsync(ct);
                return new WebhookResult(false, "Plan not found.");
            }

            txn.VnpayResponseJson = rawBody;
            await ActivatePlanForTransactionAsync(txn, ct);

            _logger.LogInformation("Webhook: payment completed for txn {TxnRef}.", txnRef);
            return new WebhookResult(true, "Success");
        }
        else
        {
            // FAILED / CANCELLED
            txn.Status = "failed";
            txn.ErrorMessage = $"Provider status: {verification.Status}";
            txn.VnpayResponseJson = rawBody;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Payment failed for txn {TxnRef}: status={Status}", txnRef, verification.Status);
            return new WebhookResult(true, "Confirmed.");
        }
    }

    /// <summary>
    /// Marks a pending transaction as expired/cancelled.
    /// Called when the user returns via PayOS cancelUrl.
    /// </summary>
    public async Task<bool> CancelTransactionAsync(string txnRef, CancellationToken ct)
    {
        var txn = await _db.PaymentTransactions
            .FirstOrDefaultAsync(pt => pt.TxnRef == txnRef, ct);

        if (txn is null || txn.Status != "pending")
            return false;

        txn.Status = "expired";
        txn.ErrorMessage = "User cancelled the payment.";
        txn.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Transaction {TxnRef} cancelled by user.", txnRef);
        return true;
    }

    /// <summary>
    /// Verifies the status of a transaction (called from return URL / payment result page).
    /// If the transaction is still pending locally, queries the payment provider for live status
    /// and activates the plan immediately if PayOS confirms payment.
    /// </summary>
    public async Task<ReturnUrlResult> VerifyReturnAsync(string txnRef, CancellationToken ct)
    {
        var txn = await _db.PaymentTransactions
            .FirstOrDefaultAsync(pt => pt.TxnRef == txnRef, ct);

        if (txn is null)
            return new ReturnUrlResult(true, "unknown", null, 0, "Transaction not found.");

        if (txn.Status == "pending"
            && txn.ExpiresAt.HasValue
            && txn.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            txn.Status = "expired";
            txn.CompletedAt = DateTimeOffset.UtcNow;
            txn.ErrorMessage = BuildExpiredMessage();
            await _db.SaveChangesAsync(ct);
        }

        // If still pending, check live status with provider — webhook may be delayed
        if (txn.Status == "pending")
        {
            try
            {
                var providerTxnId = ExtractProviderTxnIdFromJson(txn.VnpayResponseJson);
                if (!string.IsNullOrWhiteSpace(providerTxnId))
                {
                    var liveStatus = await _provider.GetTransactionStatusAsync(providerTxnId, ct);
                    if (liveStatus.Success && liveStatus.IsCompleted)
                    {
                        _logger.LogInformation(
                            "PayOS confirmed payment for txn {TxnRef} (live status: {Status}) — activating plan now.",
                            txnRef, liveStatus.Status);
                        await ActivatePlanForTransactionAsync(txn, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query provider for live status of txn {TxnRef}", txnRef);
            }
        }

        var plan = _planService.GetPlanByKey(txn.PlanKey);
        return new ReturnUrlResult(true, txn.Status, plan?.DisplayName, txn.AmountVnd, txn.ErrorMessage);
    }

    /// <summary>
    /// Activates a user plan for a confirmed paid transaction.
    /// Shared by webhook processing and return-URL verification.
    /// </summary>
    private async Task ActivatePlanForTransactionAsync(PaymentTransaction txn, CancellationToken ct)
    {
        var plan = _planService.GetPlanByKey(txn.PlanKey);
        if (plan is null)
        {
            _logger.LogError("Plan {PlanKey} not found during activation for txn {TxnRef}", txn.PlanKey, txn.TxnRef);
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Deactivate existing active plans
        var activePlans = await _db.UserPlans
            .Where(up => up.UserId == txn.UserId && up.Status == "active")
            .ToListAsync(ct);
        foreach (var ap in activePlans) ap.Status = "deactivated";

        // Calculate expiry
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

        await _db.SaveChangesAsync(ct);

        try
        {
            _audit.Add(
                null,
                "PlanPaymentCompleted",
                "UserPlan",
                userPlan.Id.ToString(),
                severity: "Medium",
                contextJson: JsonSerializer.Serialize(new
                {
                    paymentTransactionId = txn.Id,
                    provider = _provider.ProviderName,
                    status = "completed",
                }),
                ipAddress: null,
                requestId: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log audit event for payment completion");
        }

        await tx.CommitAsync(ct);
        _logger.LogInformation(
            "Payment activated for txn {TxnRef}. UserPlan {UserPlanId}.", txn.TxnRef, userPlan.Id);
    }

    /// <summary>
    /// Expires stale pending payments that have exceeded the configured expiry window.
    /// </summary>
    public async Task<int> ExpireStalePaymentsAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_settings.ExpireMinutes);
        var expired = await _db.PaymentTransactions
            .Where(pt => pt.Status == "pending" && pt.CreatedAt < cutoff)
            .ToListAsync(ct);

        var count = 0;
        foreach (var txn in expired)
        {
            txn.Status = "expired";
            txn.ErrorMessage = BuildExpiredMessage();
            count++;
        }

        if (count > 0) await _db.SaveChangesAsync(ct);
        return count;
    }

    private static string? ExtractProviderTxnIdFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("providerTxnId", out var id)
                ? id.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractPaymentUrlFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("paymentUrl", out var url)
                ? url.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveBaseUrl()
    {
        // For local development, uses localhost. In production, this should come from config.
        var envUrl = Environment.GetEnvironmentVariable("DemoUi__BackendBaseUrl");
        if (!string.IsNullOrWhiteSpace(envUrl))
            return envUrl.TrimEnd('/');

        return "http://localhost:5240";
    }

    private string BuildExpiredMessage() => $"Payment expired after {_settings.ExpireMinutes} minutes.";
}

/// <summary>
/// Lightweight result for webhook processing.
/// </summary>
public sealed record WebhookResult(bool Success, string Message);
