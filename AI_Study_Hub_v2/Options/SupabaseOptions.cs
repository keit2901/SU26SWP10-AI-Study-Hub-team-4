namespace AI_Study_Hub_v2.Options;

/// <summary>
/// Configuration for Supabase Auth (GoTrue).
/// Hosted Supabase uses JWKS-backed ES256 validation; local self-hosted GoTrue can use
/// the legacy HS256 shared secret mode. Issuer and audience must match the token claims;
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

    public SupabaseJwtValidationMode JwtValidationMode { get; set; } = SupabaseJwtValidationMode.Jwks;

    public string JwtSecret { get; set; } = string.Empty;

    public string JwtIssuer { get; set; } = string.Empty;

    public string JwtAudience { get; set; } = "authenticated";

    public int AccessTokenSeconds { get; set; } = 900;

    public bool HasValidLegacyJwtSecret() =>
        !string.IsNullOrWhiteSpace(JwtSecret) && JwtSecret.Length >= 32;
}

public enum SupabaseJwtValidationMode
{
    Jwks,
    LegacyHs256,
}
