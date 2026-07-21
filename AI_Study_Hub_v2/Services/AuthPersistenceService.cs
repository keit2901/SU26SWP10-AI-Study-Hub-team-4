using AI_Study_Hub_v2.Dtos;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace AI_Study_Hub_v2.Services;

public sealed class AuthPersistenceService
{
    private const string StorageKey = "auth_session";
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(1);
    private readonly ProtectedLocalStorage _storage;
    private readonly AuthApiClient _authApi;

    public AuthPersistenceService(ProtectedLocalStorage storage, AuthApiClient authApi)
    {
        _storage = storage;
        _authApi = authApi;
    }

    public async Task SaveAsync(AuthResponse session)
    {
        try
        {
            await _storage.SetAsync(StorageKey, session);
        }
        catch
        {
        }
    }

    public async Task<AuthResponse?> TryRestoreAsync()
    {
        try
        {
            var result = await _storage.GetAsync<AuthResponse>(StorageKey);
            if (!result.Success || result.Value is null)
            {
                return null;
            }

            var session = result.Value;
            if (session.ExpiresAt > DateTimeOffset.UtcNow.Add(RefreshWindow))
            {
                return session;
            }

            if (string.IsNullOrWhiteSpace(session.RefreshToken))
            {
                return session.ExpiresAt > DateTimeOffset.UtcNow ? session : null;
            }

            var refreshed = await _authApi.RefreshAsync(new RefreshTokenRequest
            {
                RefreshToken = session.RefreshToken
            });
            refreshed.Password = session.Password;
            await SaveAsync(refreshed);
            return refreshed;
        }
        catch (AuthApiException)
        {
            await ClearAsync();
        }
        catch
        {
        }
        return null;
    }

    public async Task ClearAsync()
    {
        try
        {
            await _storage.DeleteAsync(StorageKey);
        }
        catch
        {
        }
    }
}
