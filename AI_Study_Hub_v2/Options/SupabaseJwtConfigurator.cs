using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AI_Study_Hub_v2.Options;

public static class SupabaseJwtConfigurator
{
    private static readonly TimeSpan OidcBackchannelTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan AutomaticRefreshInterval = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan LastKnownGoodLifetime = TimeSpan.FromMinutes(10);

    public static void Configure(
        JwtBearerOptions options,
        SupabaseOptions supabase,
        bool isDevelopment,
        HttpClient? backchannel = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(supabase);

        if (!Enum.IsDefined(supabase.JwtValidationMode))
        {
            throw new InvalidOperationException("Supabase:JwtValidationMode must be Jwks or LegacyHs256.");
        }

        options.RequireHttpsMetadata = !isDevelopment;
        options.SaveToken = true;
        options.RefreshOnIssuerKeyNotFound = true;
        options.IncludeErrorDetails = isDevelopment;

        var validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = supabase.JwtIssuer,
            ValidAudience = supabase.JwtAudience,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
        };

        switch (supabase.JwtValidationMode)
        {
            case SupabaseJwtValidationMode.Jwks:
                options.Authority = supabase.JwtIssuer;
                options.ConfigurationManager = CreateJwksConfigurationManager(supabase.JwtIssuer, isDevelopment, backchannel);
                validation.ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256];
                break;

            case SupabaseJwtValidationMode.LegacyHs256:
                if (!supabase.HasValidLegacyJwtSecret())
                {
                    throw new InvalidOperationException(
                        "Supabase:JwtSecret is required and must be at least 32 characters when Supabase:JwtValidationMode is LegacyHs256.");
                }

                validation.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabase.JwtSecret));
                validation.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
                break;

            default:
                throw new InvalidOperationException("Supabase:JwtValidationMode must be Jwks or LegacyHs256.");
        }

        options.TokenValidationParameters = validation;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    SupabaseRoleClaimPromotion.Promote(identity);
                }

                return Task.CompletedTask;
            },
        };
    }

    internal static ConfigurationManager<OpenIdConnectConfiguration> CreateJwksConfigurationManager(
        string jwtIssuer,
        bool isDevelopment,
        HttpClient? backchannel = null)
    {
        var metadataAddress = $"{jwtIssuer.TrimEnd('/')}/.well-known/openid-configuration";
        var documentRetriever = new HttpDocumentRetriever(backchannel ?? new HttpClient
        {
            Timeout = OidcBackchannelTimeout,
        })
        {
            RequireHttps = !isDevelopment,
        };

        return new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            documentRetriever)
        {
            AutomaticRefreshInterval = AutomaticRefreshInterval,
            RefreshInterval = RefreshInterval,
            UseLastKnownGoodConfiguration = true,
            LastKnownGoodLifetime = LastKnownGoodLifetime,
        };
    }
}

public static class SupabaseRoleClaimPromotion
{
    public static void Promote(ClaimsIdentity identity)
    {
        var appMetaRole = identity.FindFirst("app_metadata")?.Value;
        if (string.IsNullOrEmpty(appMetaRole))
        {
            return;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(appMetaRole);
            if (document.RootElement.TryGetProperty("role", out var roleProperty) &&
                roleProperty.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var role = roleProperty.GetString();
                if (!string.IsNullOrEmpty(role) && !identity.HasClaim(ClaimTypes.Role, role))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }
        }
        catch
        {
            // Ignore malformed app_metadata exactly as the previous authentication handler did.
        }
    }
}
