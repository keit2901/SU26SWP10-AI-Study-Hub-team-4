namespace AI_Study_Hub_v2.Dtos;

public sealed record PaymentUrlResponse(
    string PaymentUrl,
    string TxnRef,
    string PlanKey,
    string BillingCycle,
    long AmountVnd,
    DateTimeOffset ExpiresAt);

public sealed record IpnResult(
    string RspCode,
    string Message);

public sealed record ReturnUrlResult(
    bool IsValid,
    string Status,
    string? PlanDisplayName,
    long AmountVnd,
    string? ErrorMessage);

public sealed record CancelPaymentResponse(
    bool Cancelled);

public sealed record PurchaseRetryRequest(
    string TxnRef);
