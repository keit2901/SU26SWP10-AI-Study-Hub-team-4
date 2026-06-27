using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public sealed class CommunityApiClient
{
    private readonly HttpClient _http;

    public CommunityApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task ReportFolderAsync(
        string accessToken,
        Guid folderId,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/community/report")
        {
            Content = JsonContent.Create(new CreateReportRequest(folderId, reason))
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            return;
        }
        await ThrowFromResponseAsync(resp, ct);
    }

    /// <summary>
    /// Admin: list all pending community reports.
    /// GET /api/community/reports/pending
    /// </summary>
    public async Task<IReadOnlyList<CommunityReportDto>> GetPendingReportsAsync(
        string accessToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Get, "api/community/reports/pending");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var result = await resp.Content.ReadFromJsonAsync<IReadOnlyList<CommunityReportDto>>(cancellationToken: ct);
            return result ?? Array.Empty<CommunityReportDto>();
        }
        await ThrowFromResponseAsync(resp, ct);
        return Array.Empty<CommunityReportDto>(); // unreachable
    }

    /// <summary>
    /// Admin: resolve or dismiss a pending community report.
    /// PATCH /api/community/reports/{reportId}/resolve
    /// </summary>
    public async Task ResolveReportAsync(
        string accessToken,
        Guid reportId,
        string status,
        string? resolution,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Patch, $"api/community/reports/{reportId}/resolve")
        {
            Content = JsonContent.Create(new ResolveReportRequest(status, resolution))
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            return;
        }
        await ThrowFromResponseAsync(resp, ct);
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
                throw new DocumentApiException(status, err.Code ?? "request_failed", message, err.Errors);
            }
        }
        catch (DocumentApiException) { throw; }
        catch
        {
            // fall through to generic
        }
        var raw = await resp.Content.ReadAsStringAsync(ct);
        throw new DocumentApiException(status, "request_failed", string.IsNullOrWhiteSpace(raw) ? $"Request failed ({status})." : raw);
    }
}
