using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class GeminiChatCompletionClient : IAiChatCompletionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
    private const int MaxHttpAttempts = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiChatCompletionClient> _logger;

    public GeminiChatCompletionClient(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        ILogger<GeminiChatCompletionClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> CompleteAsync(
        AiChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new AiChatProviderException("missing_api_key", "Gemini API key is not configured.");
        }

        for (var attempt = 1; attempt <= MaxHttpAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(RequestTimeout);

                var payloadJson = JsonSerializer.Serialize(BuildPayload(request), JsonOptions);

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildGenerateContentUri(request.ModelName))
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, MediaTypeNames.Application.Json)
                };
                httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

                using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var completion = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(body, JsonOptions);
                    var answer = completion?.Candidates
                        ?.FirstOrDefault()
                        ?.Content
                        ?.Parts
                        ?.FirstOrDefault()
                        ?.Text
                        ?.Trim();

                    // Empty completions are content failures. The caller owns one bounded repair attempt.
                    return answer ?? string.Empty;
                }

                if (IsTransientStatusCode(response.StatusCode) && attempt < MaxHttpAttempts)
                {
                    _logger.LogWarning(
                        "Gemini transient HTTP {StatusCode} on attempt {Attempt}; retrying once.",
                        (int)response.StatusCode,
                        attempt);
                    await Task.Delay(GetRetryDelay(response), cancellationToken);
                    continue;
                }

                if ((int)response.StatusCode >= 500 && attempt == 0)
                {
                    _logger.LogWarning(
                        "Gemini server error HTTP {StatusCode} (attempt 1). Retrying once.", (int)response.StatusCode);
                    await Task.Delay(1_000, cancellationToken);
                    continue;
                }

                _logger.LogWarning("Gemini chat completion failed with HTTP {StatusCode}.", (int)response.StatusCode);
                throw new AiChatProviderException(
                    "gemini_http_error",
                    $"Gemini chat completion failed with HTTP {(int)response.StatusCode}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (attempt < MaxHttpAttempts)
            {
                _logger.LogWarning("Gemini request timed out on attempt {Attempt}; retrying once.", attempt);
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (AiChatProviderException)
            {
                throw;
            }
            catch (HttpRequestException ex) when (attempt < MaxHttpAttempts)
            {
                _logger.LogWarning(ex, "Gemini transport failure on attempt {Attempt}; retrying once.", attempt);
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Gemini request timed out after {Timeout}s.", RequestTimeout.TotalSeconds);
                throw new AiChatProviderException("gemini_timeout", "Gemini request timed out.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Gemini transport failure after {Attempts} attempts.", MaxHttpAttempts);
                throw new AiChatProviderException("gemini_unavailable", "Gemini is unavailable after retries.", innerException: ex);
            }
            catch (JsonException ex)
            {
                throw new AiChatProviderException("gemini_invalid_response", "Gemini returned an invalid response.", innerException: ex);
            }
        }

        throw new AiChatProviderException("gemini_unavailable", "Gemini is unavailable after retries.");
    }

    private static bool IsTransientStatusCode(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.TooManyRequests ||
        statusCode is System.Net.HttpStatusCode.InternalServerError
            or System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.ServiceUnavailable
            or System.Net.HttpStatusCode.GatewayTimeout;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        return retryAfter is { } delay && delay > TimeSpan.Zero && delay <= TimeSpan.FromSeconds(2)
            ? delay
            : RetryDelay;
    }

    private Uri BuildGenerateContentUri(string? requestedModel)
    {
        var model = string.IsNullOrWhiteSpace(requestedModel) ? _options.Model : requestedModel;
        return new Uri($"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_options.ApiKey}");
    }

    private object BuildPayload(AiChatCompletionRequest request)
    {
        return new
        {
            system_instruction = new
            {
                parts = new[] { new { text = request.SystemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = request.UserPrompt } }
                }
            },
            generationConfig = new
            {
                temperature = _options.Temperature,
                maxOutputTokens = request.MaxTokens ?? (_options.MaxTokens > 0 ? _options.MaxTokens : 1024),
            }
        };
    }

    private sealed record GeminiGenerateContentResponse(IReadOnlyList<GeminiCandidate>? Candidates);

    private sealed record GeminiCandidate(GeminiContent? Content);

    private sealed record GeminiContent(IReadOnlyList<GeminiPart>? Parts);

    private sealed record GeminiPart(string? Text);
}
