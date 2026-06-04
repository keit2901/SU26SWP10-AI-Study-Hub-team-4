using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public interface ITurnstileVerificationService
{
    bool IsEnabled { get; }

    bool IsConfigured { get; }

    Task<TurnstileVerificationResult> VerifyAsync(
        string? token,
        string? remoteIp = null,
        string? expectedAction = null,
        CancellationToken cancellationToken = default);
}

public sealed record TurnstileVerificationResult(
    bool Success,
    string Message,
    IReadOnlyList<string> ErrorCodes)
{
    public static TurnstileVerificationResult Valid(string message = "Verification passed.")
        => new(true, message, Array.Empty<string>());

    public static TurnstileVerificationResult Invalid(string message, IReadOnlyList<string>? errorCodes = null)
        => new(false, message, errorCodes ?? Array.Empty<string>());
}

public sealed class TurnstileVerificationService : ITurnstileVerificationService
{
    private readonly HttpClient _http;
    private readonly TurnstileOptions _options;
    private readonly ILogger<TurnstileVerificationService> _logger;

    public TurnstileVerificationService(
        HttpClient http,
        IOptions<TurnstileOptions> options,
        ILogger<TurnstileVerificationService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<TurnstileVerificationResult> VerifyAsync(
        string? token,
        string? remoteIp = null,
        string? expectedAction = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return TurnstileVerificationResult.Valid("Turnstile is disabled for this environment.");
        }

        if (!_options.IsConfigured)
        {
            return TurnstileVerificationResult.Invalid("Turnstile is enabled but missing SiteKey or SecretKey configuration.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return TurnstileVerificationResult.Invalid("Complete the Cloudflare Turnstile challenge before continuing.", new[] { "missing-input-response" });
        }

        if (token.Length > 2048)
        {
            return TurnstileVerificationResult.Invalid("Turnstile response token is too long.", new[] { "invalid-input-response" });
        }

        var idempotencyKey = Guid.NewGuid().ToString("N");
        var payload = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey,
            ["response"] = token,
            ["idempotency_key"] = idempotencyKey
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            payload["remoteip"] = remoteIp;
        }

        try
        {
            using var response = await _http.PostAsync(
                _options.VerifyEndpoint,
                new FormUrlEncodedContent(payload),
                cancellationToken);

            var body = await response.Content.ReadFromJsonAsync<TurnstileSiteVerifyResponse>(cancellationToken);
            if (body is null)
            {
                return TurnstileVerificationResult.Invalid("Turnstile verification returned an empty response.", new[] { "bad-request" });
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Turnstile verification endpoint returned HTTP {StatusCode} with errors: {Errors}",
                    (int)response.StatusCode,
                    string.Join(',', body.ErrorCodes));
                return TurnstileVerificationResult.Invalid("Turnstile verification failed. Please retry the challenge.", body.ErrorCodes);
            }

            if (!body.Success)
            {
                return TurnstileVerificationResult.Invalid("Turnstile verification failed. Please retry the challenge.", body.ErrorCodes);
            }

            if (!string.IsNullOrWhiteSpace(expectedAction) &&
                !string.Equals(body.Action, expectedAction, StringComparison.OrdinalIgnoreCase))
            {
                return TurnstileVerificationResult.Invalid("Turnstile action did not match this form.", new[] { "action-mismatch" });
            }

            return TurnstileVerificationResult.Valid("Cloudflare Turnstile verification passed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TurnstileVerificationResult.Invalid("Turnstile verification timed out. Please retry.", new[] { "internal-error" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Turnstile verification failed due to a network or parsing error.");
            return TurnstileVerificationResult.Invalid("Turnstile verification is temporarily unavailable. Please retry.", new[] { "internal-error" });
        }
    }

    private sealed class TurnstileSiteVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("challenge_ts")]
        public DateTimeOffset? ChallengeTimestamp { get; set; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("error-codes")]
        public string[] ErrorCodes { get; set; } = Array.Empty<string>();
    }
}
