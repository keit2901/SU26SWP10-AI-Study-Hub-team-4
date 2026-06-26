using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class RecaptchaVerificationService : IRecaptchaVerificationService
{
    private readonly HttpClient _http;
    private readonly RecaptchaOptions _options;
    private readonly ILogger<RecaptchaVerificationService> _logger;

    public RecaptchaVerificationService(
        HttpClient http,
        IOptions<RecaptchaOptions> options,
        ILogger<RecaptchaVerificationService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public bool IsConfigured => _options.IsConfigured;

    public bool ShouldVerify => _options.Enabled && !_options.AllowDevelopmentFallback;


    public async Task<RecaptchaVerificationResult> VerifyAsync(
        string? token,
        string? remoteIp = null,
        string? expectedAction = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return RecaptchaVerificationResult.Valid("reCAPTCHA is disabled for this environment.");
        }

        if (_options.AllowDevelopmentFallback)
        {
            return RecaptchaVerificationResult.Valid("reCAPTCHA verification bypassed (development fallback).");
        }


        if (!_options.IsConfigured)
        {
            return RecaptchaVerificationResult.Invalid("reCAPTCHA is enabled but missing SiteKey or SecretKey configuration.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return RecaptchaVerificationResult.Invalid("Complete the reCAPTCHA challenge before continuing.", new[] { "missing-input-response" });
        }

        if (token.Length > 10000)
        {
            return RecaptchaVerificationResult.Invalid("reCAPTCHA response token is too long.", new[] { "invalid-input-response" });
        }

        var payload = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey,
            ["response"] = token
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

            var body = await response.Content.ReadFromJsonAsync<RecaptchaSiteVerifyResponse>(cancellationToken);
            if (body is null)
            {
                return RecaptchaVerificationResult.Invalid("reCAPTCHA verification returned an empty response.", new[] { "bad-request" });
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("reCAPTCHA verification endpoint returned HTTP {StatusCode} with errors: {Errors}",
                    (int)response.StatusCode,
                    string.Join(',', body.ErrorCodes));
                return RecaptchaVerificationResult.Invalid("reCAPTCHA verification failed. Please retry the challenge.", body.ErrorCodes);
            }

            if (!body.Success)
            {
                return RecaptchaVerificationResult.Invalid("reCAPTCHA verification failed. Please retry the challenge.", body.ErrorCodes);
            }

            return RecaptchaVerificationResult.Valid("reCAPTCHA verification passed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RecaptchaVerificationResult.Invalid("reCAPTCHA verification timed out. Please retry.", new[] { "internal-error" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "reCAPTCHA verification failed due to a network or parsing error.");
            return RecaptchaVerificationResult.Invalid("reCAPTCHA verification is temporarily unavailable. Please retry.", new[] { "internal-error" });
        }
    }

    private sealed class RecaptchaSiteVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("challenge_ts")]
        public DateTimeOffset? ChallengeTimestamp { get; set; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; }

        [JsonPropertyName("error-codes")]
        public string[] ErrorCodes { get; set; } = Array.Empty<string>();
    }
}
