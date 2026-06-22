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
    private static readonly int[] RetryDelaysMs = [1_000, 2_000, 4_000];

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

        for (var attempt = 0; attempt <= RetryDelaysMs.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(RequestTimeout);

                var payloadJson = JsonSerializer.Serialize(BuildPayload(request), JsonOptions);

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildGenerateContentUri())
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, MediaTypeNames.Application.Json)
                };
                httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

                using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

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

                    if (string.IsNullOrWhiteSpace(answer))
                    {
                        throw new AiChatProviderException("gemini_empty_response", "Gemini returned an empty answer.");
                    }

                    return answer;
                }

                if ((int)response.StatusCode == 429 && attempt < RetryDelaysMs.Length)
                {
                    var delay = RetryDelaysMs[attempt];
                    _logger.LogWarning(
                        "Gemini rate limited (attempt {Attempt}). Retrying in {Delay}ms.", attempt + 1, delay);
                    await Task.Delay(delay, cancellationToken);
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Gemini request timed out after {Timeout}s.", RequestTimeout.TotalSeconds);
                throw new AiChatProviderException("gemini_timeout", "Gemini request timed out.");
            }
            catch (AiChatProviderException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < RetryDelaysMs.Length)
            {
                _logger.LogWarning(ex, "Gemini transient failure (attempt {Attempt}). Retrying.", attempt + 1);
                await Task.Delay(RetryDelaysMs[attempt], cancellationToken);
            }
        }

        throw new AiChatProviderException("gemini_unavailable", "Gemini is unavailable after retries.");
    }

    private Uri BuildGenerateContentUri()
    {
        var model = string.IsNullOrWhiteSpace(_options.Model) ? "gemini-2.5-flash" : _options.Model;
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
