using Microsoft.AspNetCore.Http;

namespace AI_Study_Hub_v2.Services.Payment;

public interface IVnPayService
{
    Task<Dtos.PaymentUrlResponse> CreatePaymentAsync(
        Guid userId, string planKey, string billingCycle,
        string userIpAddress, CancellationToken ct);

    Task<Dtos.IpnResult> ProcessIpnCallbackAsync(
        IQueryCollection queryParams, CancellationToken ct);

    Task<Dtos.ReturnUrlResult> VerifyReturnUrlAsync(
        IQueryCollection queryParams, CancellationToken ct);

    Task<int> ExpireStalePaymentsAsync(CancellationToken ct);
}
