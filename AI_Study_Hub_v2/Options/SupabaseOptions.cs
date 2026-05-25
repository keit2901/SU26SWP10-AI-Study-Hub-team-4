namespace AI_Study_Hub_v2.Options;

/// <summary>
/// Configuration for the local Supabase Auth (GoTrue) instance reached through Kong.
/// JwtSecret + Issuer + Audience must match what GoTrue itself is signing tokens with;
/// AnonKey is the public apikey required for non-admin GoTrue endpoints; ServiceRoleKey
/// is required for admin endpoints (e.g. /auth/v1/admin/users) and must never be sent
/// to the browser.
/// </summary>
public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = string.Empty;

    public string AnonKey { get; set; } = string.Empty;

    public string ServiceRoleKey { get; set; } = string.Empty;

    public string JwtSecret { get; set; } = string.Empty;

    public string JwtIssuer { get; set; } = string.Empty;

    public string JwtAudience { get; set; } = "authenticated";

    public int AccessTokenSeconds { get; set; } = 900;
}
