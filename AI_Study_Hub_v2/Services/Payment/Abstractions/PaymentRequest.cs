namespace AI_Study_Hub_v2.Services.Payment.Abstractions;

/// <summary>
/// Request to create a payment link via a provider.
/// </summary>
public sealed record PaymentRequest(
    Guid UserId,
    string TxnRef,
    long AmountVnd,
    string Description,
    string ReturnUrl,
    string CancelUrl);
