using System.Security.Claims;
using AI_Study_Hub_v2.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AI_Study_Hub_v2.Tests.Options;

[TestFixture]
public sealed class SupabaseJwtConfiguratorTests
{
    [Test]
    public void Configure_JwksMode_UsesBoundedOidcManagerAndEs256WithoutSymmetricKey()
    {
        var options = new JwtBearerOptions();

        SupabaseJwtConfigurator.Configure(options, CreateOptions(SupabaseJwtValidationMode.Jwks, jwtSecret: string.Empty), isDevelopment: false);

        options.Authority.Should().Be("https://issuer.example.test/auth/v1");
        options.RequireHttpsMetadata.Should().BeTrue();
        options.RefreshOnIssuerKeyNotFound.Should().BeTrue();
        options.IncludeErrorDetails.Should().BeFalse();
        var manager = options.ConfigurationManager.Should().BeOfType<ConfigurationManager<OpenIdConnectConfiguration>>().Subject;
        manager.MetadataAddress.Should().Be("https://issuer.example.test/auth/v1/.well-known/openid-configuration");
        manager.AutomaticRefreshInterval.Should().Be(TimeSpan.FromMinutes(10));
        manager.RefreshInterval.Should().Be(TimeSpan.FromMinutes(1));
        manager.UseLastKnownGoodConfiguration.Should().BeTrue();
        manager.LastKnownGoodLifetime.Should().Be(TimeSpan.FromMinutes(10));
        options.TokenValidationParameters.IssuerSigningKey.Should().BeNull();
        options.TokenValidationParameters.ValidAlgorithms.Should().Equal(SecurityAlgorithms.EcdsaSha256);
    }

    [Test]
    public void Configure_JwksMode_UsesStrictValidationWithoutRequiringSecret()
    {
        var options = new JwtBearerOptions();

        SupabaseJwtConfigurator.Configure(options, CreateOptions(SupabaseJwtValidationMode.Jwks, jwtSecret: string.Empty), isDevelopment: false);

        var validation = options.TokenValidationParameters;
        validation.ValidateIssuer.Should().BeTrue();
        validation.ValidIssuer.Should().Be("https://issuer.example.test/auth/v1");
        validation.ValidateAudience.Should().BeTrue();
        validation.ValidAudience.Should().Be("authenticated");
        validation.ValidateLifetime.Should().BeTrue();
        validation.ValidateIssuerSigningKey.Should().BeTrue();
        validation.RequireSignedTokens.Should().BeTrue();
        validation.RequireExpirationTime.Should().BeTrue();
        validation.ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void Configure_LegacyHs256Mode_UsesSymmetricKeyAndHs256Only()
    {
        var options = new JwtBearerOptions();

        SupabaseJwtConfigurator.Configure(options, CreateOptions(SupabaseJwtValidationMode.LegacyHs256, new string('s', 32)), isDevelopment: true);

        options.Authority.Should().BeNull();
        options.ConfigurationManager.Should().BeNull();
        options.TokenValidationParameters.IssuerSigningKey.Should().BeOfType<SymmetricSecurityKey>();
        options.TokenValidationParameters.ValidAlgorithms.Should().Equal(SecurityAlgorithms.HmacSha256);
    }

    [TestCase("")]
    [TestCase("too-short")]
    public void Configure_LegacyHs256Mode_RejectsMissingOrShortSecret(string jwtSecret)
    {
        var configure = () => SupabaseJwtConfigurator.Configure(
            new JwtBearerOptions(),
            CreateOptions(SupabaseJwtValidationMode.LegacyHs256, jwtSecret),
            isDevelopment: true);

        configure.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void PromoteRoleFromAppMetadata_IsSafeAndIdempotent()
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("app_metadata", "{\"role\":\"Admin\"}"));

        SupabaseRoleClaimPromotion.Promote(identity);
        SupabaseRoleClaimPromotion.Promote(identity);

        identity.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Should().Equal("Admin");
    }

    [Test]
    public void PromoteRoleFromMalformedAppMetadata_DoesNotAddRole()
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("app_metadata", "not-json"));

        var promote = () => SupabaseRoleClaimPromotion.Promote(identity);

        promote.Should().NotThrow();
        identity.HasClaim(ClaimTypes.Role, "Admin").Should().BeFalse();
    }

    private static SupabaseOptions CreateOptions(SupabaseJwtValidationMode validationMode, string jwtSecret) => new()
    {
        JwtValidationMode = validationMode,
        JwtIssuer = "https://issuer.example.test/auth/v1",
        JwtAudience = "authenticated",
        JwtSecret = jwtSecret,
    };
}
