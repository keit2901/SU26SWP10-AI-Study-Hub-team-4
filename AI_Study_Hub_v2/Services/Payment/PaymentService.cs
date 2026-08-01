using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Payment.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Payment;

/// <summary>Owns the durable, provider-authoritative PayOS payment state machine.</summary>
public sealed class PaymentService : IPaymentService
{
    private readonly IPaymentProvider _provider;
    private readonly AppDbContext _db;
    private readonly IPlanService _planService;
    private readonly IAuditLogService _audit;
    private readonly PayOsSettings _settings;
    private readonly string _callbackBaseUrl;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(IPaymentProvider provider, AppDbContext db, IPlanService planService,
        IAuditLogService audit, IOptions<PayOsSettings> options, IConfiguration configuration,
        IHostEnvironment environment, ILogger<PaymentService> logger)
    {
        _provider = provider;
        _db = db;
        _planService = planService;
        _audit = audit;
        _settings = options.Value;
        _callbackBaseUrl = ResolveCallbackBaseUrl(_settings.CallbackBaseUrl,
            configuration["DemoUi:BackendBaseUrl"], environment.IsDevelopment());
        _logger = logger;
    }

    public static string ResolveCallbackBaseUrl(string? configured, string? demoBackendBaseUrl, bool isDevelopment)
    {
        if (TryValidateCallbackBaseUrl(configured, !isDevelopment, out var callback)) return callback;
        if (isDevelopment && TryValidateCallbackBaseUrl(demoBackendBaseUrl, false, out callback)) return callback;
        if (isDevelopment) return "http://localhost:5240";
        throw new InvalidOperationException("PayOs:CallbackBaseUrl must be an absolute HTTPS public URL.");
    }

    public static bool TryValidateCallbackBaseUrl(string? value, bool production, out string normalized)
    {
        normalized = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && (!production && !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || (production && (uri.IsLoopback || uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)))
            return false;
        normalized = uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/');
        return true;
    }

    public async Task<PaymentUrlResponse> CreatePaymentAsync(Guid userId, string planKey, string billingCycle, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new KeyNotFoundException("User not found.");
        var plan = _planService.GetPlanByKey(planKey) ?? throw new KeyNotFoundException("Plan not found.");
        var amount = billingCycle == "yearly" ? plan.YearlyPriceVnd ?? 0 : plan.MonthlyPriceVnd ?? 0;
        if (amount <= 0) throw new InvalidOperationException("This plan has no price configured.");

        var now = DateTimeOffset.UtcNow;
        var txnRef = PayOsProvider.GenerateTxnRef();
        if (!PayOsProvider.TryParseOrderCode(txnRef, out var orderCode)) throw new InvalidOperationException("Unable to allocate PayOS order code.");
        var transaction = new PaymentTransaction
        {
            Id = Guid.NewGuid(), UserId = user.Id, TxnRef = txnRef, ProviderOrderCode = orderCode,
            PlanKey = planKey, BillingCycle = billingCycle, AmountVnd = amount, Status = "pending",
            CreatedAt = now, ExpiresAt = now.AddMinutes(_settings.ExpireMinutes)
        };
        _db.PaymentTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);

        var callbackUrl = _callbackBaseUrl + "/payment/result";
        var providerResult = await _provider.CreatePaymentLinkAsync(new PaymentRequest(user.Id, txnRef, amount,
            $"AI Study Hub - {plan.DisplayName} {billingCycle}", callbackUrl, callbackUrl), ct);
        if (!providerResult.Success || providerResult.OrderCode != orderCode)
        {
            transaction.Status = "failed";
            transaction.ErrorMessage = "Payment gateway could not create a checkout.";
            transaction.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            throw new PaymentProviderException("The payment gateway is temporarily unavailable. Please try again in a moment.", "create_failed");
        }
        transaction.ProviderPaymentLinkId = providerResult.PaymentLinkId;
        transaction.ProviderStatus = "PENDING";
        await _db.SaveChangesAsync(ct);
        return new PaymentUrlResponse(providerResult.PaymentUrl, txnRef, planKey, billingCycle, amount, transaction.ExpiresAt!.Value);
    }

    public async Task<ReturnUrlResult?> ReconcileAsync(Guid userId, long orderCode, CancellationToken ct)
    {
        if (orderCode is <= 0 or > 9007199254740991) return null;
        var candidate = await FindOwnedAsync(userId, orderCode, false, ct, asNoTracking: true);
        if (candidate is null) return null;
        if (IsImmutable(candidate)) return ToResult(candidate);
        var providerResult = await _provider.GetTransactionStatusAsync(orderCode, ct); // external call before locks
        if (!providerResult.Success || providerResult.OrderCode != orderCode)
            return ToResult(candidate, false, candidate.Status, "Payment status is temporarily unavailable. Please retry.");
        var outcome = await ApplyAuthoritativeAsync(orderCode, userId, providerResult, null, ct);
        return outcome.Result;
    }

    public async Task<WebhookResult> ProcessWebhookAsync(string rawBody, CancellationToken ct)
    {
        string signature;
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            signature = document.RootElement.TryGetProperty("signature", out var element) ? element.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException) { return WebhookResult.Ignored; }

        WebhookVerificationResult verified;
        try { verified = await _provider.VerifyWebhookAsync(rawBody, signature, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "PayOS webhook verification transport failure."); return WebhookResult.Retryable; }
        if (!verified.IsValid || verified.OrderCode <= 0) return WebhookResult.Ignored;

        try
        {
            var outcome = await ApplyAuthoritativeAsync(verified.OrderCode, null,
                new TransactionStatusResult(true, verified.OrderCode, verified.PaymentLinkId, verified.Status,
                    verified.ProviderStatus, verified.AmountPaidVnd, verified.ExpectedAmountVnd, verified.AmountRemainingVnd), verified, ct);
            return outcome.Disposition;
        }
        catch (DbUpdateException ex) { _logger.LogWarning(ex, "PayOS webhook persistence failure."); return WebhookResult.Retryable; }
        catch (RetryableActivationException ex) { _logger.LogWarning(ex, "PayOS webhook activation is retryable."); return WebhookResult.Retryable; }
    }

    private async Task<ApplyOutcome> ApplyAuthoritativeAsync(long orderCode, Guid? ownerId,
        TransactionStatusResult provider, WebhookVerificationResult? webhook, CancellationToken ct)
    {
        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        DetachTrackedPayment(orderCode);
        var payment = await FindOwnedAsync(ownerId, orderCode, true, ct);
        if (payment is null) { if (transaction is not null) await transaction.CommitAsync(ct); return ApplyOutcome.Ignored; }
        if (IsImmutable(payment) || payment.UserPlanId.HasValue)
        {
            if (transaction is not null) await transaction.CommitAsync(ct);
            return new ApplyOutcome(WebhookResult.Idempotent, ToResult(payment));
        }
        if (provider.OrderCode != payment.ProviderOrderCode && !(payment.ProviderOrderCode is null && PayOsProvider.TryParseOrderCode(payment.TxnRef, out var legacy) && legacy == orderCode))
            return ApplyOutcome.Ignored;

        payment.ProviderOrderCode ??= orderCode;
        payment.ProviderPaymentLinkId ??= provider.PaymentLinkId;
        payment.ProviderStatus = provider.ProviderStatus.Trim().ToUpperInvariant();
        var normalized = PayOsProvider.NormalizeStatus(provider.Status);
        if (normalized == "UNKNOWN") { await SaveAndCommitAsync(transaction, ct); return new ApplyOutcome(WebhookResult.Ignored, ToResult(payment)); }
        if (normalized == "PAID")
        {
            if (payment.ErrorMessage == "integrity_failed" || payment.Status is not ("pending" or "cancelled" or "expired"))
            {
                await SaveAndCommitAsync(transaction, ct);
                return new ApplyOutcome(WebhookResult.Ignored, ToResult(payment));
            }
            if (provider.AmountPaidVnd != payment.AmountVnd || provider.ExpectedAmountVnd != payment.AmountVnd || provider.AmountRemainingVnd != 0)
            {
                payment.Status = "failed"; payment.ErrorMessage = "integrity_failed"; payment.CompletedAt = DateTimeOffset.UtcNow;
                await SaveAndCommitAsync(transaction, ct); return new ApplyOutcome(WebhookResult.Ignored, ToResult(payment));
            }
            var lockedUser = await LockUserAsync(payment.UserId, ct);
            if (lockedUser is null || !lockedUser.IsActive)
            {
                await SaveAndCommitAsync(transaction, ct);
                throw new RetryableActivationException("The local payment owner is temporarily unavailable.");
            }
            if (payment.Status == "completed" || payment.UserPlanId.HasValue) { if (transaction is not null) await transaction.CommitAsync(ct); return new ApplyOutcome(WebhookResult.Idempotent, ToResult(payment)); }
            var plan = await _db.Plans.FirstOrDefaultAsync(p => p.PlanKey == payment.PlanKey, ct);
            if (plan is null)
            {
                await SaveAndCommitAsync(transaction, ct);
                throw new RetryableActivationException("The purchased plan is temporarily unavailable.");
            }
            var now = DateTimeOffset.UtcNow;
            var activePlans = await _db.UserPlans.Where(p => p.UserId == payment.UserId && p.Status == "active").ToListAsync(ct);
            foreach (var active in activePlans) active.Status = "deactivated";
            var userPlan = new UserPlan { Id = Guid.NewGuid(), UserId = payment.UserId, PlanId = plan.Id, Status = "active", AssignedAt = now, PaidAt = now, ExpiresAt = payment.BillingCycle == "yearly" ? now.AddYears(1) : now.AddMonths(1) };
            _db.UserPlans.Add(userPlan);
            payment.Status = "completed"; payment.CompletedAt = now; payment.UserPlanId = userPlan.Id; payment.ErrorMessage = null;
            _audit.Add(null, "PlanPaymentCompleted", "UserPlan", userPlan.Id.ToString(), "Medium",
                contextJson: JsonSerializer.Serialize(new { provider = _provider.ProviderName }), ipAddress: null, requestId: null);
            await SaveAndCommitAsync(transaction, ct);
            return new ApplyOutcome(WebhookResult.Accepted, ToResult(payment));
        }
        if (normalized is "CANCELLED" or "EXPIRED" or "FAILED" && payment.Status == "pending")
        {
            payment.Status = normalized switch { "CANCELLED" => "cancelled", "EXPIRED" => "expired", _ => "failed" };
            payment.ErrorMessage = null; payment.CompletedAt = DateTimeOffset.UtcNow;
        }
        // PENDING, PROCESSING, and UNDERPAID deliberately retain the local pending lifecycle.
        await SaveAndCommitAsync(transaction, ct);
        return new ApplyOutcome(WebhookResult.Accepted, ToResult(payment));
    }

    public async Task<int> ExpireStalePaymentsAsync(CancellationToken ct)
    {
        var stale = await _db.PaymentTransactions.Where(p => p.Status == "pending" && p.ExpiresAt < DateTimeOffset.UtcNow && p.ProviderOrderCode != null).Select(p => new { p.UserId, p.ProviderOrderCode }).ToListAsync(ct);
        var count = 0;
        foreach (var item in stale)
        {
            var result = await ReconcileAsync(item.UserId, item.ProviderOrderCode.Value, ct);
            if (result?.Status == "expired") count++;
        }
        return count;
    }

    private Task<PaymentTransaction?> FindOwnedAsync(Guid? ownerId, long orderCode, bool forUpdate, CancellationToken ct, bool asNoTracking = false)
    {
        var legacyTxnRef = $"PO_{orderCode}";
        IQueryable<PaymentTransaction> query;
        if (forUpdate && _db.Database.IsNpgsql())
            query = _db.PaymentTransactions.FromSqlInterpolated($"SELECT * FROM payment_transactions WHERE (provider_order_code = {orderCode} OR (provider_order_code IS NULL AND txn_ref = {legacyTxnRef})) FOR UPDATE");
        else
            query = _db.PaymentTransactions.Where(p => p.ProviderOrderCode == orderCode || (p.ProviderOrderCode == null && p.TxnRef == legacyTxnRef));
        if (ownerId.HasValue) query = query.Where(p => p.UserId == ownerId.Value);
        if (asNoTracking) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(ct);
    }

    private async Task<User?> LockUserAsync(Guid userId, CancellationToken ct)
    {
        foreach (var entry in _db.ChangeTracker.Entries<User>().Where(entry => entry.Entity.Id == userId))
            entry.State = EntityState.Detached;
        if (_db.Database.IsNpgsql()) return await _db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE").SingleOrDefaultAsync(ct);
        return await _db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
    }

    private async Task SaveAndCommitAsync(IDbContextTransaction? transaction, CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    private ReturnUrlResult ToResult(PaymentTransaction payment, bool valid = true, string? status = null, string? message = null)
        => new(valid, status ?? payment.Status, _planService.GetPlanByKey(payment.PlanKey)?.DisplayName, payment.AmountVnd, message ?? payment.ErrorMessage, payment.ProviderStatus);

    private void DetachTrackedPayment(long orderCode)
    {
        foreach (var entry in _db.ChangeTracker.Entries<PaymentTransaction>()
                     .Where(entry => entry.Entity.ProviderOrderCode == orderCode
                         || (entry.Entity.ProviderOrderCode is null && entry.Entity.TxnRef == $"PO_{orderCode}")))
            entry.State = EntityState.Detached;
    }

    private static bool IsImmutable(PaymentTransaction payment)
        => payment.Status is "completed" or "refunded" or "demo_completed";

    private sealed record ApplyOutcome(WebhookResult Disposition, ReturnUrlResult? Result)
    {
        public static ApplyOutcome Ignored { get; } = new(WebhookResult.Ignored, null);
    }
}

public sealed class RetryableActivationException : Exception
{
    public RetryableActivationException(string message) : base(message) { }
}

public enum WebhookDisposition { Accepted, Idempotent, Ignored, RetryableFailure }
public sealed record WebhookResult(WebhookDisposition Disposition)
{
    public static WebhookResult Accepted { get; } = new(WebhookDisposition.Accepted);
    public static WebhookResult Idempotent { get; } = new(WebhookDisposition.Idempotent);
    public static WebhookResult Ignored { get; } = new(WebhookDisposition.Ignored);
    public static WebhookResult Retryable { get; } = new(WebhookDisposition.RetryableFailure);
}
