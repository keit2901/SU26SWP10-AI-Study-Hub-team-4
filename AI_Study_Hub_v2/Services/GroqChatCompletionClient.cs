using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class GroqChatCompletionClient : IAiChatCompletionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxHttpAttempts = 2;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly HttpClient _httpClient;
    private readonly GroqOptions _options;
    private readonly ILogger<GroqChatCompletionClient> _logger;

    public GroqChatCompletionClient(
        HttpClient httpClient,
        IOptions<GroqOptions> options,
        ILogger<GroqChatCompletionClient> logger)
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
            throw new AiChatProviderException("missing_api_key", "Groq API key is not configured.");
        }

        for (var attempt = 1; attempt <= MaxHttpAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(RequestTimeout);
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri())
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(BuildPayload(request), JsonOptions),
                        Encoding.UTF8,
                        MediaTypeNames.Application.Json)
                };
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

                using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    if (IsTransientStatusCode(response.StatusCode) && attempt < MaxHttpAttempts)
                    {
                        _logger.LogWarning("Groq transient HTTP {StatusCode} on attempt {Attempt}; retrying once.", statusCode, attempt);
                        await Task.Delay(GetRetryDelay(response), cancellationToken);
                        continue;
                    }

                    _logger.LogWarning("Groq chat completion failed with HTTP {StatusCode}.", statusCode);
                    var code = statusCode == 429 ? "groq_rate_limited" : "groq_http_error";
                    throw new AiChatProviderException(code, $"Groq chat completion failed with HTTP {statusCode}.", statusCode: statusCode);
                }

                var completion = JsonSerializer.Deserialize<GroqChatCompletionResponse>(body, JsonOptions);
                // Empty completions are content failures. The caller owns one bounded repair attempt.
                return completion?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (attempt < MaxHttpAttempts)
            {
                _logger.LogWarning("Groq request timed out on attempt {Attempt}; retrying once.", attempt);
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxHttpAttempts)
            {
                _logger.LogWarning(ex, "Groq transport failure on attempt {Attempt}; retrying once.", attempt);
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Groq transport failure after {Attempts} attempts.", MaxHttpAttempts);
                throw new AiChatProviderException("groq_unavailable", "Groq is unavailable after retries.", innerException: ex);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Groq request timed out after {Timeout}s.", RequestTimeout.TotalSeconds);
                throw new AiChatProviderException("groq_timeout", "Groq request timed out.");
            }
            catch (JsonException ex)
            {
                throw new AiChatProviderException("groq_invalid_response", "Groq returned an invalid response.", innerException: ex);
            }
        }

        throw new AiChatProviderException("groq_unavailable", "Groq is unavailable after retries.");
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

    private Uri BuildChatCompletionsUri()
    {
        var endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
            ? "https://api.groq.com/openai/v1"
            : _options.Endpoint.Trim();

        if (!endpoint.EndsWith('/'))
        {
            endpoint += "/";
        }

        return new Uri(new Uri(endpoint), "chat/completions");
    }

    private object BuildPayload(AiChatCompletionRequest request)
    {
        var model = string.IsNullOrWhiteSpace(request.ModelName) ? _options.Model : request.ModelName;
        return new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt },
            },
            temperature = _options.Temperature,
            max_tokens = request.MaxTokens ?? (_options.MaxTokens > 0 ? _options.MaxTokens : 1024),
        };
    }

    private sealed record GroqChatCompletionResponse(IReadOnlyList<GroqChoice>? Choices);

    private sealed record GroqChoice(GroqMessage? Message);

    private sealed record GroqMessage(string? Content);
}
