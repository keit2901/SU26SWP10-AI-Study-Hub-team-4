namespace AI_Study_Hub_v2.Services.Payment.Abstractions;

/// <summary>
/// Result of querying a transaction status from a provider.
/// </summary>
public sealed record TransactionStatusResult(
    bool Success,
    long OrderCode,
    string? PaymentLinkId,
    string Status,
    string ProviderStatus,
    long AmountPaidVnd,
    long ExpectedAmountVnd,
    long AmountRemainingVnd);
