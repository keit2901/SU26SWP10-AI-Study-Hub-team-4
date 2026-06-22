using AI_Study_Hub_v2.Dtos;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace AI_Study_Hub_v2.Services;

public sealed class AuthPersistenceService
{
    private const string StorageKey = "auth_session";
    private readonly ProtectedSessionStorage _storage;

    public AuthPersistenceService(ProtectedSessionStorage storage)
    {
        _storage = storage;
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
            if (result.Success && result.Value is not null)
                return result.Value;
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
