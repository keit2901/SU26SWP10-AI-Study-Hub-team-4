using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public sealed class AdminDashboardApiClient
{
    private readonly HttpClient _http;

    public AdminDashboardApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<AdminDashboardStatsDto> GetAdminStatsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/dashboard/admin/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<AdminDashboardStatsDto>(cancellationToken: cancellationToken)
                ?? throw new DocumentApiException(500, "empty_response", "Server returned an empty response.");
        }

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: cancellationToken);
        throw new DocumentApiException(
            (int)response.StatusCode,
            error?.Code ?? "unknown",
            error?.Message ?? response.ReasonPhrase ?? "Request failed");
    }

    public async Task<UserDashboardStatsDto> GetUserStatsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/dashboard/user/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<UserDashboardStatsDto>(cancellationToken: cancellationToken)
                ?? throw new DocumentApiException(500, "empty_response", "Server returned an empty response.");
        }

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: cancellationToken);
        throw new DocumentApiException(
            (int)response.StatusCode,
            error?.Code ?? "unknown",
            error?.Message ?? response.ReasonPhrase ?? "Request failed");
    }

    public async Task<ActivityTrendsDto> GetActivityTrendsAsync(
        string accessToken,
        string period = "day",
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/dashboard/admin/activity-trends?period={period}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<ActivityTrendsDto>(cancellationToken: cancellationToken)
                ?? throw new DocumentApiException(500, "empty_response", "Server returned an empty response.");
        }

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: cancellationToken);
        throw new DocumentApiException(
            (int)response.StatusCode,
            error?.Code ?? "unknown",
            error?.Message ?? response.ReasonPhrase ?? "Request failed");
    }
}
