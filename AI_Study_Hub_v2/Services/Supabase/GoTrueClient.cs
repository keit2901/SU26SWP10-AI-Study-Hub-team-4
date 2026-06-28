using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Supabase;

/// <summary>
/// Typed-HttpClient implementation of <see cref="IGoTrueClient"/>. The HttpClient is
/// configured in Program.cs with BaseAddress = {Supabase:Url}/auth/v1/ and the
/// "apikey" header set to the anon key. Admin endpoints additionally inject the
/// service role key as a Bearer token.
/// </summary>
public sealed class GoTrueClient : IGoTrueClient
{
    private readonly HttpClient _http;
    private readonly SupabaseOptions _options;

    public GoTrueClient(HttpClient http, IOptions<SupabaseOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<GoTrueSession> SignUpAsync(string email, string password, Dictionary<string, object?>? metadata, CancellationToken cancellationToken = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            "signup",
            new SignUpRequest { Email = email, Password = password, UserMetadata = metadata },
            cancellationToken);
        return await ParseSessionOrThrowAsync(resp, "signup", cancellationToken);
    }

    public async Task<GoTrueSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            "token?grant_type=password",
            new PasswordGrantRequest { Email = email, Password = password },
            cancellationToken);
        return await ParseSessionOrThrowAsync(resp, "login", cancellationToken);
    }

    public async Task<GoTrueSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            "token?grant_type=refresh_token",
            new RefreshGrantRequest { RefreshToken = refreshToken },
            cancellationToken);
        return await ParseSessionOrThrowAsync(resp, "refresh", cancellationToken);
    }

    public async Task SignOutAsync(string accessToken, bool global, CancellationToken cancellationToken = default)
    {
        var path = global ? "logout?scope=global" : "logout?scope=local";
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await _http.SendAsync(req, cancellationToken);
        if (resp.StatusCode == HttpStatusCode.NoContent || resp.IsSuccessStatusCode)
        {
            return;
        }
        await ThrowFromGoTrueAsync(resp, "logout", cancellationToken);
    }

    public async Task<GoTrueUser> GetUserAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "user");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await _http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            await ThrowFromGoTrueAsync(resp, "get_user", cancellationToken);
        }
        var user = await resp.Content.ReadFromJsonAsync<GoTrueUser>(cancellationToken: cancellationToken);
        return user ?? throw new AuthException(500, "gotrue_empty_response", "GoTrue returned empty user.");
    }

    public async Task<GoTrueUser> AdminCreateUserAsync(string email, string password, Dictionary<string, object?>? userMetadata, Dictionary<string, object?>? appMetadata, CancellationToken cancellationToken = default)
    {
        using var req = BuildAdminRequest(HttpMethod.Post, "admin/users");
        req.Content = JsonContent.Create(new AdminCreateUserRequest
        {
            Email = email,
            Password = password,
            EmailConfirm = true,
            UserMetadata = userMetadata,
            AppMetadata = appMetadata,
        });
        using var resp = await _http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            await ThrowFromGoTrueAsync(resp, "admin_create_user", cancellationToken);
        }
        var user = await resp.Content.ReadFromJsonAsync<GoTrueUser>(cancellationToken: cancellationToken);
        return user ?? throw new AuthException(500, "gotrue_empty_response", "GoTrue returned empty user from admin create.");
    }

    public async Task<GoTrueUser?> AdminGetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // GoTrue admin list endpoint supports `email=` filter via query string.
        var path = $"admin/users?filter={Uri.EscapeDataString(email)}";
        using var req = BuildAdminRequest(HttpMethod.Get, path);
        using var resp = await _http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            await ThrowFromGoTrueAsync(resp, "admin_list_users", cancellationToken);
        }

        using var doc = await JsonDocument.ParseAsync(
            await resp.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("users", out var usersElem) || usersElem.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var lower = email.Trim().ToLowerInvariant();
        foreach (var u in usersElem.EnumerateArray())
        {
            var emailField = u.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : null;
            if (string.Equals(emailField, lower, StringComparison.OrdinalIgnoreCase))
            {
                return u.Deserialize<GoTrueUser>();
            }
        }
        return null;
    }

    public async Task<GoTrueUser> UpdateUserAsync(string accessToken, string? email, string? password, Dictionary<string, object?>? userMetadata, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, "user");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var body = new Dictionary<string, object?>();
        if (email is not null) body["email"] = email;
        if (password is not null) body["password"] = password;
        if (userMetadata is not null) body["data"] = userMetadata;

        req.Content = JsonContent.Create(body);

        using var resp = await _http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            await ThrowFromGoTrueAsync(resp, "update_user", cancellationToken);
        }
        var user = await resp.Content.ReadFromJsonAsync<GoTrueUser>(cancellationToken: cancellationToken);
        return user ?? throw new AuthException(500, "gotrue_empty_response", "GoTrue returned empty user from update.");
    }

    private HttpRequestMessage BuildAdminRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, path);
        // service role key as Bearer overrides the default apikey header set by HttpClient defaults.
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        // Some GoTrue versions also require the apikey header explicitly.
        if (req.Headers.TryGetValues("apikey", out _) is false)
        {
            req.Headers.TryAddWithoutValidation("apikey", _options.ServiceRoleKey);
        }
        return req;
    }

    private static async Task<GoTrueSession> ParseSessionOrThrowAsync(HttpResponseMessage resp, string operation, CancellationToken cancellationToken)
    {
        if (!resp.IsSuccessStatusCode)
        {
            await ThrowFromGoTrueAsync(resp, operation, cancellationToken);
        }
        var session = await resp.Content.ReadFromJsonAsync<GoTrueSession>(cancellationToken: cancellationToken);
        return session ?? throw new AuthException(500, "gotrue_empty_response", "GoTrue returned empty session.");
    }

    private static async Task ThrowFromGoTrueAsync(HttpResponseMessage resp, string operation, CancellationToken cancellationToken)
    {
        var status = (int)resp.StatusCode;
        string code = MapDefaultCode(operation, status);
        string message = $"GoTrue {operation} failed ({status}).";

        try
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var err = JsonSerializer.Deserialize<GoTrueErrorResponse>(body);
                    if (err is not null)
                    {
                        var msg = err.Message ?? err.ErrorDescription ?? err.Error ?? body;
                        var c = err.ErrorCode ?? err.Code ?? code;
                        message = msg!;
                        code = c!;
                    }
                }
                catch
                {
                    message = body;
                }
            }
        }
        catch
        {
            // ignore
        }

        // Re-map common GoTrue codes to controller-friendly codes.
        var translatedStatus = status;
        var translatedCode = code;
        var translatedMessage = message;
        if (operation == "login" && status == 400)
        {
            translatedStatus = 401;
            translatedCode = "invalid_credentials";
            translatedMessage = "Email or password is incorrect.";
        }
        else if (operation == "signup" && (status == 422 || status == 400) && (code?.Contains("registered", StringComparison.OrdinalIgnoreCase) == true || message.Contains("registered", StringComparison.OrdinalIgnoreCase)))
        {
            translatedStatus = 409;
            translatedCode = "email_already_registered";
            translatedMessage = "Email is already registered.";
        }
        else if (operation == "refresh" && (status == 400 || status == 401))
        {
            translatedStatus = 401;
            translatedCode = "invalid_refresh_token";
            translatedMessage = "Refresh token is invalid or has been revoked.";
        }

        throw new AuthException(translatedStatus, translatedCode, translatedMessage);
    }

    private static string MapDefaultCode(string operation, int status) => (operation, status) switch
    {
        ("login", _)  => "login_failed",
        ("signup", _) => "signup_failed",
        ("refresh", _) => "refresh_failed",
        ("logout", _) => "logout_failed",
        ("get_user", _) => "get_user_failed",
        ("admin_create_user", _) => "admin_create_failed",
        ("admin_list_users", _) => "admin_list_failed",
        ("update_user", _) => "update_user_failed",
        _ => "gotrue_request_failed",
    };
}
