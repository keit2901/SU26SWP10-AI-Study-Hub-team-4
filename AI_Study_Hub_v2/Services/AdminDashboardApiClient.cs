using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

        await ThrowError(response, cancellationToken);
        throw new InvalidOperationException();
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

        await ThrowError(response, cancellationToken);
        throw new InvalidOperationException();
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

        await ThrowError(response, cancellationToken);
        throw new InvalidOperationException();
    }

    public Task<List<DocumentDto>> GetPendingModerationDocumentsAsync(string accessToken, Guid? folderId = null, CancellationToken ct = default) =>
        SendAsync<List<DocumentDto>>(HttpMethod.Get, $"api/dashboard/moderation/documents{Query(folderId)}", accessToken, ct);

    public async Task ApproveDocumentAsync(string accessToken, Guid documentId, CancellationToken ct = default)
    {
        await SendNoContentAsync(HttpMethod.Post, $"api/dashboard/moderation/documents/{documentId}/approve", accessToken, ct);
    }

    public async Task RejectDocumentAsync(string accessToken, Guid documentId, string? reason = null, CancellationToken ct = default)
    {
        await SendNoContentAsync(HttpMethod.Post, $"api/dashboard/moderation/documents/{documentId}/reject", accessToken, ct,
            JsonContent.Create(new { Reason = reason }));
    }

    public Task<DocumentAiReviewResultDto> AiReviewDocumentAsync(string accessToken, Guid documentId, CancellationToken ct = default) =>
        SendAsync<DocumentAiReviewResultDto>(HttpMethod.Post, $"api/dashboard/moderation/documents/{documentId}/ai-review", accessToken, ct);

    public Task<UserAnalyticsDto> GetModerationAnalyticsAsync(string accessToken, Guid? folderId, int page, int pageSize, CancellationToken ct = default) =>
        SendAsync<UserAnalyticsDto>(HttpMethod.Get,
            $"api/dashboard/moderation/analytics{Query(folderId)}&page={page}&pageSize={pageSize}", accessToken, ct);

    public Task<string> GetModerationDocumentSignedUrlAsync(string accessToken, Guid documentId, CancellationToken ct = default) =>
        SendAsync<string>(HttpMethod.Get, $"api/dashboard/moderation/documents/{documentId}/signed-url", accessToken, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, string accessToken, CancellationToken ct, HttpContent? content = null)
    {
        using var request = CreateAuth(method, uri, accessToken);
        request.Content = content;
        using var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                ?? throw new DocumentApiException(500, "empty_response", "Server returned an empty response.");
        await ThrowError(response, ct);
        throw new InvalidOperationException();
    }

    private async Task SendNoContentAsync(HttpMethod method, string uri, string accessToken, CancellationToken ct, HttpContent? content = null)
    {
        using var request = CreateAuth(method, uri, accessToken);
        request.Content = content;
        using var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return;
        await ThrowError(response, ct);
    }

    private static HttpRequestMessage CreateAuth(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static string Query(Guid? folderId) => folderId.HasValue ? $"?folderId={folderId.Value}" : "?";

    private static async Task ThrowError(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var error = JsonSerializer.Deserialize<ApiErrorResponse>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (error is not null && (!string.IsNullOrWhiteSpace(error.Code) || !string.IsNullOrWhiteSpace(error.Message)))
                {
                    throw new DocumentApiException(
                        status,
                        string.IsNullOrWhiteSpace(error.Code) ? "request_failed" : error.Code,
                        string.IsNullOrWhiteSpace(error.Message) ? $"Request failed ({status})." : error.Message,
                        error.Errors);
                }
            }
        }
        catch (DocumentApiException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            // Fall through to a stable typed exception for malformed bodies.
        }
        catch
        {
            // Fall through to a stable typed exception if the body cannot be read.
        }

        throw new DocumentApiException(status, "request_failed",
            string.IsNullOrWhiteSpace(response.ReasonPhrase) ? $"Request failed ({status})." : response.ReasonPhrase);
    }
}
