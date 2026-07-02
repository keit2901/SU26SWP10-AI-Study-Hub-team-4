using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public sealed class BenchmarkApiClient
{
    private readonly HttpClient _http;

    public BenchmarkApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<BenchmarkHistoryItemDto>> GetHistoryAsync(
        string accessToken,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/benchmark/history?take={take}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<IReadOnlyList<BenchmarkHistoryItemDto>>(cancellationToken: cancellationToken)
                ?? [];
        }

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: cancellationToken);
        throw new DocumentApiException(
            (int)response.StatusCode,
            error?.Code ?? "unknown",
            error?.Message ?? response.ReasonPhrase ?? "Request failed");
    }

    public async Task RunManualAsync(
        string accessToken,
        string? modelName = null,
        int? count = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/benchmark/run");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            modelName,
            count
        });

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: cancellationToken);
        throw new DocumentApiException(
            (int)response.StatusCode,
            error?.Code ?? "unknown",
            error?.Message ?? response.ReasonPhrase ?? "Request failed");
    }

    public async Task RunAutomationAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/benchmark/automation/run-now");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: cancellationToken);
        throw new DocumentApiException(
            (int)response.StatusCode,
            error?.Code ?? "unknown",
            error?.Message ?? response.ReasonPhrase ?? "Request failed");
    }
}
