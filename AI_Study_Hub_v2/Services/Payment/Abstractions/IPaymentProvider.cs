namespace AI_Study_Hub_v2.Services.Payment.Abstractions;

/// <summary>
/// Provider-neutral interface for payment gateway integration.
/// Each provider (PayOS, VNPay, etc.) implements this interface.
/// </summary>
public interface IPaymentProvider
{
    string ProviderName { get; }

    Task<PaymentLinkResult> CreatePaymentLinkAsync(
        PaymentRequest request, CancellationToken ct = default);

    Task<WebhookVerificationResult> VerifyWebhookAsync(
        string rawBody, string signature, CancellationToken ct = default);

    Task<TransactionStatusResult> GetTransactionStatusAsync(
        string providerTransactionId, CancellationToken ct = default);
}
