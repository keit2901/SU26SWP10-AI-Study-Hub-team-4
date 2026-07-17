namespace AI_Study_Hub_v2.Services.Payment.Abstractions;

/// <summary>
/// Result of webhook data verification from a payment provider.
/// </summary>
public sealed record WebhookVerificationResult(
    bool IsValid,
    string ProviderTxnId,
    string Status,       // "PAID" | "CANCELLED" | "EXPIRED"
    long AmountVnd,
    string? ErrorMessage);
