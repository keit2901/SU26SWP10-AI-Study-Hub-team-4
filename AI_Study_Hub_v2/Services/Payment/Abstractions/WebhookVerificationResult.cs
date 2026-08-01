namespace AI_Study_Hub_v2.Services.Payment.Abstractions;

/// <summary>
/// Result of webhook data verification from a payment provider.
/// </summary>
public sealed record WebhookVerificationResult(
    bool IsValid,
    long OrderCode,
    string? PaymentLinkId,
    string Status,       // "PAID" | "CANCELLED" | "EXPIRED"
    string ProviderStatus,
    long AmountPaidVnd,
    long ExpectedAmountVnd,
    long AmountRemainingVnd,
    string? ErrorMessage);
