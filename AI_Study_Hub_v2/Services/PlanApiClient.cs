using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        string accessToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Get, "api/plans");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

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
    public async Task<UserPlanDto> PurchasePlanAsync(
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
            return await resp.Content.ReadFromJsonAsync<UserPlanDto>(cancellationToken: ct)
                ?? throw new PlanApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    private static async Task ThrowFromResponseAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var status = (int)resp.StatusCode;
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: ct);
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
        var raw = await resp.Content.ReadAsStringAsync(ct);
        throw new PlanApiException(status, "request_failed", string.IsNullOrWhiteSpace(raw) ? $"Request failed ({status})." : raw);
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
