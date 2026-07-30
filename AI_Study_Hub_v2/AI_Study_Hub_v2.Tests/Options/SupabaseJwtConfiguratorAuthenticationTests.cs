using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI_Study_Hub_v2.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace AI_Study_Hub_v2.Tests.Options;

[TestFixture]
public sealed class SupabaseJwtConfiguratorAuthenticationTests
{
    private const string Issuer = "https://issuer.example.test/auth/v1";
    private const string Audience = "authenticated";
    private const string KeyId = "test-p256-key";

    [Test]
    public async Task JwksMode_ValidEs256TokenWithMatchingClaims_IsAcceptedAndPromotesRole()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var backchannel = CreateOidcBackchannel(key);
        using var server = CreateServer(backchannel);
        using var client = server.CreateClient();

        var response = await SendAsync(client, CreateEs256Token(key, Issuer, Audience, "Admin"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Admin");
    }

    [Test]
    public async Task JwksMode_Hs256Token_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var backchannel = CreateOidcBackchannel(key);
        using var server = CreateServer(backchannel);
        using var client = server.CreateClient();

        var response = await SendAsync(client, CreateHs256Token(Issuer, Audience));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task JwksMode_TokenWithWrongIssuer_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var backchannel = CreateOidcBackchannel(key);
        using var server = CreateServer(backchannel);
        using var client = server.CreateClient();

        var response = await SendAsync(client, CreateEs256Token(key, "https://other-issuer.example.test", Audience));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task JwksMode_TokenWithWrongAudience_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var backchannel = CreateOidcBackchannel(key);
        using var server = CreateServer(backchannel);
        using var client = server.CreateClient();

        var response = await SendAsync(client, CreateEs256Token(key, Issuer, "other-audience"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static TestServer CreateServer(HttpClient backchannel)
    {
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services
                    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options => SupabaseJwtConfigurator.Configure(
                        options,
                        new SupabaseOptions
                        {
                            JwtValidationMode = SupabaseJwtValidationMode.Jwks,
                            JwtIssuer = Issuer,
                            JwtAudience = Audience,
                        },
                        isDevelopment: false,
                        backchannel));
                services.AddAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapGet("/secure", async context =>
                {
                    await context.Response.WriteAsync(context.User.IsInRole("Admin") ? "Admin" : "Missing role");
                }).RequireAuthorization());
            }));
    }

    private static HttpClient CreateOidcBackchannel(ECDsa key)
    {
        var publicParameters = key.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    kid = KeyId,
                    use = "sig",
                    alg = "ES256",
                    x = Base64UrlEncoder.Encode(publicParameters.Q.X!),
                    y = Base64UrlEncoder.Encode(publicParameters.Q.Y!),
                },
            },
        });
        var discovery = JsonSerializer.Serialize(new { issuer = Issuer, jwks_uri = $"{Issuer}/keys" });

        return new HttpClient(new OidcDocumentHandler(discovery, jwks));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static string CreateEs256Token(ECDsa key, string issuer, string audience, string? role = null)
    {
        var claims = role is null
            ? null
            : new[] { new System.Security.Claims.Claim("app_metadata", $"{{\"role\":\"{role}\"}}") };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = claims is null ? null : new System.Security.Claims.ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = KeyId }, SecurityAlgorithms.EcdsaSha256),
        });
    }

    private static string CreateHs256Token(string issuer, string audience) =>
        new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('h', 32))),
                SecurityAlgorithms.HmacSha256),
        });

    private sealed class OidcDocumentHandler(string discovery, string jwks) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsolutePath.EndsWith("/keys", StringComparison.Ordinal) == true
                ? jwks
                : discovery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }
}
