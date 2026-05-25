namespace AI_Study_Hub_v2.Services.Supabase;

/// <summary>
/// Minimal client for the GoTrue REST API used by the local Supabase stack.
/// Phase 1 surfaces only password grant + refresh + signup + global signout +
/// admin-create-user. We deliberately avoid the supabase-csharp SDK to keep
/// the dependency surface small.
/// </summary>
public interface IGoTrueClient
{
    Task<GoTrueSession> SignUpAsync(string email, string password, Dictionary<string, object?>? metadata, CancellationToken cancellationToken = default);

    Task<GoTrueSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<GoTrueSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke the access token's session. Scope=global revokes ALL sessions of the user.
    /// </summary>
    Task SignOutAsync(string accessToken, bool global, CancellationToken cancellationToken = default);

    Task<GoTrueUser> GetUserAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Admin endpoint — requires service-role key. Creates an already-confirmed user.</summary>
    Task<GoTrueUser> AdminCreateUserAsync(string email, string password, Dictionary<string, object?>? userMetadata, Dictionary<string, object?>? appMetadata, CancellationToken cancellationToken = default);

    /// <summary>Admin endpoint — fetch a user by email. Returns null when not found.</summary>
    Task<GoTrueUser?> AdminGetUserByEmailAsync(string email, CancellationToken cancellationToken = default);
}
