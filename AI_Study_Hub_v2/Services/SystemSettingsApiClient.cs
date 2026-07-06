using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public sealed class SystemSettingsApiClient
{
    private readonly HttpClient _http;

    public SystemSettingsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<SystemConfigDto>> GetAllAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/admin/settings", accessToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<List<SystemConfigDto>>(cancellationToken: cancellationToken)
                ?? new List<SystemConfigDto>();
        }
        await ThrowFromResponseAsync(response, cancellationToken);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<SystemConfigDto> UpdateValueAsync(
        string accessToken,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Put, $"api/admin/settings/{key}", accessToken);
        request.Content = JsonContent.Create(new UpdateSystemConfigRequest { Value = value });

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<SystemConfigDto>(cancellationToken: cancellationToken)
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
