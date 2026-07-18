namespace AI_Study_Hub_v2.Services.Payment.Abstractions;

/// <summary>
/// Result from creating a payment link.
/// </summary>
public sealed record PaymentLinkResult(
    bool Success,
    string PaymentUrl,
    string ProviderTxnId,
    string? ErrorMessage);
