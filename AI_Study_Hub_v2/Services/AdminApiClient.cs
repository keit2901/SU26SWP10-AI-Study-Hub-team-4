using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public sealed class AdminApiClient
{
    private readonly HttpClient _http;

    public AdminApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<IReadOnlyList<AdminUserDto>> ListUsersAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
        => GetListAsync<AdminUserDto>("api/admin/users", accessToken, cancellationToken);

    public async Task<AdminUserDto> UpdateQuotaAsync(
        string accessToken,
        Guid userId,
        long dailyTokenQuota,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"api/admin/users/{userId}/quota",
            accessToken);
        request.Content = JsonContent.Create(new UpdateUserQuotaRequest
        {
            DailyTokenQuota = dailyTokenQuota,
        });

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<AdminUserDto>(cancellationToken: cancellationToken)
                ?? throw new DocumentApiException(500, "empty_response", "Server returned an empty response.");
        }
        await ThrowFromResponseAsync(response, cancellationToken);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<AdminUserDto> UpdateRoleAsync(
        string accessToken,
        Guid userId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"api/admin/users/{userId}/role",
            accessToken);
        request.Content = JsonContent.Create(new UpdateUserRoleRequest
        {
            Role = roleName,
        });

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<AdminUserDto>(cancellationToken: cancellationToken)
                ?? throw new DocumentApiException(500, "empty_response", "Server returned an empty response.");
        }
        await ThrowFromResponseAsync(response, cancellationToken);
        throw new InvalidOperationException("Unreachable");
    }

    public Task<IReadOnlyList<AuditLogDto>> ListAuditLogsAsync(
        string accessToken,
        int limit = 200,
        CancellationToken cancellationToken = default)
        => GetListAsync<AuditLogDto>(
            $"api/admin/audit-logs?limit={Math.Clamp(limit, 1, 500)}",
            accessToken,
            cancellationToken);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(
        string url,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, url, accessToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken: cancellationToken)
                ?? new List<T>();
        }
        await ThrowFromResponseAsync(response, cancellationToken);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<AdminUserDto> ToggleActiveAsync(
        string accessToken,
        Guid userId,
        bool activate,
        CancellationToken cancellationToken = default)
    {
        var action = activate ? "activate" : "deactivate";
        using var request = CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"api/admin/users/{userId}/{action}",
            accessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<AdminUserDto>(cancellationToken: cancellationToken)
                ?? throw new DocumentApiException(500, "empty_response", "Server returned an empty response.");
        }
        await ThrowFromResponseAsync(response, cancellationToken);
        throw new InvalidOperationException("Unreachable");
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string url,
        string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task ThrowFromResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(
                cancellationToken: cancellationToken);
            if (error is not null)
            {
                throw new DocumentApiException(
                    status,
                    error.Code ?? "request_failed",
                    string.IsNullOrWhiteSpace(error.Message)
                        ? $"Request failed with status {status}."
                        : error.Message,
                    error.Errors);
            }
        }
        catch (DocumentApiException)
        {
            throw;
        }
        catch
        {
            // Fall through to a generic response below.
        }

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new DocumentApiException(
            status,
            "request_failed",
            string.IsNullOrWhiteSpace(raw) ? $"Request failed ({status})." : raw);
    }
}
