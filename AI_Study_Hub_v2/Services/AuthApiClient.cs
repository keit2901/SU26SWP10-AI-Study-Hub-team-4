using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Thin wrapper around the AuthController endpoints, used by Blazor pages.
/// Throws <see cref="AuthApiException"/> with the server-provided error code/message
/// so the UI can show a friendly toast.
/// </summary>
public sealed class AuthApiClient
{
    private readonly HttpClient _http;

    public AuthApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
        => PostAsync<RegisterRequest, AuthResponse>("api/auth/register", request, accessToken: null, ct);

    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        => PostAsync<LoginRequest, AuthResponse>("api/auth/login", request, accessToken: null, ct);

    public async Task LogoutAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NoContent)
        {
            return;
        }
        await ThrowFromResponseAsync(resp, ct);
    }

    public async Task<UserDto> GetMeAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<UserDto>(cancellationToken: ct);
            return dto ?? throw new AuthApiException(500, "empty_response", "Server returned empty response.");
        }
        await ThrowFromResponseAsync(resp, ct);
        throw new InvalidOperationException("Unreachable");
    }

    private async Task<TResp> PostAsync<TReq, TResp>(string path, TReq body, string? accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        if (!string.IsNullOrEmpty(accessToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<TResp>(cancellationToken: ct);
            return dto ?? throw new AuthApiException(500, "empty_response", "Server returned empty response.");
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
                throw new AuthApiException(status, err.Code ?? "request_failed", message, err.Errors);
            }
        }
        catch (AuthApiException) { throw; }
        catch
        {
            // fall through to generic
        }
        var raw = await resp.Content.ReadAsStringAsync(ct);
        throw new AuthApiException(status, "request_failed", string.IsNullOrWhiteSpace(raw) ? $"Request failed ({status})." : raw);
    }
}

public sealed class AuthApiException : Exception
{
    public AuthApiException(int statusCode, string code, string message, IDictionary<string, string[]>? errors = null)
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
