namespace AI_Study_Hub_v2.Services.Payment.Abstractions;

/// <summary>
/// Result of querying a transaction status from a provider.
/// </summary>
public sealed record TransactionStatusResult(
    bool Success,
    string Status,
    long AmountVnd,
    bool IsCompleted);
