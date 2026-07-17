using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Thin wrapper around the PlansController endpoints used by Blazor pages.
/// Follows the same pattern as <see cref="DocumentApiClient"/>.
/// </summary>
public sealed class PlanApiClient
{
    private readonly HttpClient _http;

    public PlanApiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Fetches the list of active plans.</summary>
    public async Task<IReadOnlyList<PlanDto>> GetPlansAsync(
        string? accessToken = null,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/plans");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var plans = await resp.Content.ReadFromJsonAsync<List<PlanDto>>(cancellationToken: ct);
            return plans ?? new List<PlanDto>();
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Fetches the calling user's current plan and storage quota snapshot.</summary>
    public async Task<StorageQuotaSnapshotDto> GetCurrentPlanAsync(
        string accessToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Get, "api/plans/current");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            return await resp.Content.ReadFromJsonAsync<StorageQuotaSnapshotDto>(cancellationToken: ct)
                ?? throw new PlanApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Purchases (or upgrades to) a plan for the calling user.</summary>
    public async Task<PaymentUrlResponse> PurchasePlanAsync(
        string accessToken,
        string planKey,
        string billingCycle = "monthly",
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        idempotencyKey ??= Guid.NewGuid().ToString("N");

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/plans/purchase");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = JsonContent.Create(new { planKey, billingCycle, idempotencyKey });

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            return await resp.Content.ReadFromJsonAsync<PaymentUrlResponse>(cancellationToken: ct)
                ?? throw new PlanApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Activates a free plan immediately (no payment).</summary>
    public async Task<UserPlanDto> PurchaseFreePlanAsync(
        string accessToken,
        string planKey,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/plans/purchase");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = JsonContent.Create(new { planKey, billingCycle = "monthly" });

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            return await resp.Content.ReadFromJsonAsync<UserPlanDto>(cancellationToken: ct)
                ?? throw new PlanApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Retries payment for a pending transaction.</summary>
    public async Task<PaymentUrlResponse> RetryPaymentAsync(
        string accessToken,
        string txnRef,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(txnRef);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/plans/purchase/retry/{txnRef}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            return await resp.Content.ReadFromJsonAsync<PaymentUrlResponse>(cancellationToken: ct)
                ?? throw new PlanApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    /// <summary>Verifies VNPay return URL query parameters.</summary>
    public async Task<ReturnUrlResult> VerifyReturnUrlAsync(
        string? accessToken,
        string queryString,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"api/vnpay/return{queryString}");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            return await resp.Content.ReadFromJsonAsync<ReturnUrlResult>(cancellationToken: ct)
                ?? throw new PlanApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    private static async Task ThrowFromResponseAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var raw = await resp.Content.ReadAsStringAsync(ct);
        var status = (int)resp.StatusCode;

        try
        {
            var err = JsonSerializer.Deserialize<ApiErrorResponse>(raw);
            if (err is not null && (!string.IsNullOrEmpty(err.Code) || !string.IsNullOrEmpty(err.Message)))
            {
                var message = !string.IsNullOrWhiteSpace(err.Message)
                    ? err.Message
                    : $"Request failed with status {status}.";
                throw new PlanApiException(status, err.Code ?? "request_failed", message, err.Errors);
            }
        }
        catch (PlanApiException) { throw; }
        catch
        {
            // fall through to generic
        }

        throw new PlanApiException(status, "request_failed",
            string.IsNullOrWhiteSpace(raw) ? $"Request failed ({status})." : raw);
    }
}

public sealed class PlanApiException : Exception
{
    public PlanApiException(int statusCode, string code, string message, IDictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public IDictionary<string, string[]>? Errors { get; }
}
