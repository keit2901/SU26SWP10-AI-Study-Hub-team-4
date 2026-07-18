using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services.Payment.Abstractions;

/// <summary>
/// Business logic interface for payment operations.
/// </summary>
public interface IPaymentService
{
    Task<PaymentUrlResponse> CreatePaymentAsync(
        Guid userId, string planKey, string billingCycle, CancellationToken ct);

    Task<WebhookResult> ProcessWebhookAsync(string rawBody, CancellationToken ct);

    Task<ReturnUrlResult> VerifyReturnAsync(string txnRef, CancellationToken ct);

    Task<bool> CancelTransactionAsync(string txnRef, CancellationToken ct);

    Task<int> ExpireStalePaymentsAsync(CancellationToken ct);
}
