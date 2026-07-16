using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public sealed class EscalationApiClient
{
    private readonly HttpClient _http;

    public EscalationApiClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<DocumentEscalationDto>> GetPendingAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Get, "api/admin/escalations", accessToken);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
            return await resp.Content.ReadFromJsonAsync<List<DocumentEscalationDto>>(cancellationToken: ct) ?? new();
        await ThrowError(resp, ct);
        throw new InvalidOperationException();
    }

    public async Task<IReadOnlyList<DocumentEscalationDto>> GetAllAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Get, "api/admin/escalations/all", accessToken);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
            return await resp.Content.ReadFromJsonAsync<List<DocumentEscalationDto>>(cancellationToken: ct) ?? new();
        await ThrowError(resp, ct);
        throw new InvalidOperationException();
    }

    public async Task<IReadOnlyList<DocumentEscalationDto>> GetMyAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Get, "api/admin/escalations/my", accessToken);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
            return await resp.Content.ReadFromJsonAsync<List<DocumentEscalationDto>>(cancellationToken: ct) ?? new();
        await ThrowError(resp, ct);
        throw new InvalidOperationException();
    }

    public async Task<DocumentEscalationDto> CreateAsync(string accessToken, CreateEscalationRequest request, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Post, "api/admin/escalations", accessToken);
        req.Content = JsonContent.Create(request);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
            return await resp.Content.ReadFromJsonAsync<DocumentEscalationDto>(cancellationToken: ct)
                ?? throw new DocumentApiException(500, "empty_response", "Empty response");
        await ThrowError(resp, ct);
        throw new InvalidOperationException();
    }

    public async Task<DocumentEscalationDto> ResolveAsync(string accessToken, Guid id, ResolveEscalationRequest request, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Patch, $"api/admin/escalations/{id}", accessToken);
        req.Content = JsonContent.Create(request);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
            return await resp.Content.ReadFromJsonAsync<DocumentEscalationDto>(cancellationToken: ct)
                ?? throw new DocumentApiException(500, "empty_response", "Empty response");
        await ThrowError(resp, ct);
        throw new InvalidOperationException();
    }

    private static HttpRequestMessage CreateAuth(HttpMethod m, string url, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var r = new HttpRequestMessage(m, url);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return r;
    }

    private static async Task ThrowError(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: ct);
            if (err is not null)
                throw new DocumentApiException((int)resp.StatusCode, err.Code ?? "error", err.Message ?? "Error");
        }
        catch (DocumentApiException) { throw; }
        catch { }
        throw new DocumentApiException((int)resp.StatusCode, "error", $"Request failed ({resp.StatusCode})");
    }
}
