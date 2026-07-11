using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public sealed class FolderApiClient
{
    private readonly HttpClient _http;

    public FolderApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<FolderDto>> ListAsync(string accessToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Get, "api/folders");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var rows = await resp.Content.ReadFromJsonAsync<List<FolderDto>>(cancellationToken: ct);
            return rows ?? new List<FolderDto>();
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<FolderDto> CreateAsync(
        string accessToken,
        CreateFolderRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(request);

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/folders")
        {
            Content = JsonContent.Create(request)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<FolderDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<FolderDto> UpdateAsync(
        string accessToken,
        Guid id,
        UpdateFolderRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(request);

        using var req = new HttpRequestMessage(HttpMethod.Put, $"api/folders/{id}")
        {
            Content = JsonContent.Create(request)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<FolderDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<IReadOnlyList<FolderDto>> ListSharedAsync(
        string? accessToken = null,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/folders/shared");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var rows = await resp.Content.ReadFromJsonAsync<List<FolderDto>>(cancellationToken: ct);
            return rows ?? new List<FolderDto>();
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<IReadOnlyList<FolderDto>> ListPersonalSharedAsync(string accessToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Get, "api/folders/personal-shared");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var rows = await resp.Content.ReadFromJsonAsync<List<FolderDto>>(cancellationToken: ct);
            return rows ?? new List<FolderDto>();
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<FolderDto> CopySharedFolderAsync(string accessToken, Guid id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/folders/{id}/copy");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<FolderDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<FolderDto> VoteAsync(string accessToken, Guid id, bool isLike, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/folders/{id}/vote")
        {
            Content = JsonContent.Create(new { isLike })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<FolderDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<FolderDto> ToggleFavoriteAsync(string accessToken, Guid id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Patch, $"api/folders/{id}/favorite");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<FolderDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<FolderDto> RequestShareAsync(string accessToken, Guid id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Patch, $"api/folders/{id}/share");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<FolderDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task<FolderDto> AppealShareReviewAsync(
        string accessToken,
        Guid id,
        AppealFolderShareRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(request);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"api/folders/{id}/share/appeal")
        {
            Content = JsonContent.Create(request)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<FolderDto>(cancellationToken: ct);
            return dto ?? throw new DocumentApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    public async Task DeleteAsync(string accessToken, Guid id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var req = new HttpRequestMessage(HttpMethod.Delete, $"api/folders/{id}");
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
