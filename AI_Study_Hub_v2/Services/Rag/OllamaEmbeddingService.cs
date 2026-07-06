using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag;

public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private long _totalRequests;
    private long _failedRequests;
    private int _loggedConfiguration;

    public OllamaEmbeddingService(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("Ollama BaseUrl is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("Ollama model is required.");
        }

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _loggedConfiguration, 1) == 0)
        {
            _logger.LogInformation(
                "Ollama embedding configured: model={Model}, base_url={BaseUrl}, timeout_seconds={TimeoutSeconds}",
                _options.Model,
                _options.BaseUrl,
                _options.TimeoutSeconds);
        }

        var prompt = text ?? string.Empty;
        var maxAttempts = Math.Max(1, _options.MaxRetries);
        Exception? lastException = null;
        Interlocked.Increment(ref _totalRequests);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var request = new OllamaEmbeddingRequest(_options.Model, prompt);

                using var response = await _httpClient.PostAsJsonAsync(
                    "/api/embeddings",
                    request,
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
                    cancellationToken: cancellationToken);

                if (result?.Embedding is null)
                {
                    throw new InvalidOperationException("Ollama response does not contain embedding.");
                }

                ValidateEmbedding(result.Embedding);
                stopwatch.Stop();

                _logger.LogInformation(
                    "Embedding generated: model={Model}, latency_ms={LatencyMs}, prompt_length={PromptLength}, attempt={Attempt}",
                    _options.Model,
                    stopwatch.ElapsedMilliseconds,
                    prompt.Length,
                    attempt);

                if (stopwatch.ElapsedMilliseconds > 5000)
                {
                    _logger.LogWarning(
                        "Embedding latency exceeded threshold: model={Model}, latency_ms={LatencyMs}",
                        _options.Model,
                        stopwatch.ElapsedMilliseconds);
                }

                return result.Embedding;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                lastException = ex;

                if (attempt >= maxAttempts)
                {
                    break;
                }

                var delaySeconds = (int)Math.Pow(2, attempt - 1);

                _logger.LogWarning(
                    ex,
                    "Ollama embedding attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelaySeconds}s. latency_ms={LatencyMs}",
                    attempt,
                    maxAttempts,
                    delaySeconds,
                    stopwatch.ElapsedMilliseconds);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        var failures = Interlocked.Increment(ref _failedRequests);
        var total = Math.Max(1, Interlocked.Read(ref _totalRequests));
        var failureRate = failures * 100d / total;

        _logger.LogError(
            lastException,
            "Embedding request failed after retries: model={Model}, failure_rate_percent={FailureRatePercent:F2}, total_requests={TotalRequests}",
            _options.Model,
            failureRate,
            total);

        throw new AiChatException(
            503,
            "embedding_service_unavailable",
            "Dá»‹ch vá»¥ embedding Ä‘ang báº£o trÃ¬, vui lÃ²ng thá»­ láº¡i sau.");
    }

    private static void ValidateEmbedding(float[] embedding)
    {
        if (embedding.Length != DocumentChunk.EmbeddingDimension)
        {
            throw new InvalidOperationException(
                $"Ollama embedding dimension must be {DocumentChunk.EmbeddingDimension}, but got {embedding.Length}.");
        }

        if (!embedding.Any(value => value != 0f))
        {
            throw new InvalidOperationException("Ollama returned a zero-vector embedding.");
        }
    }

    private sealed record OllamaEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private sealed record OllamaEmbeddingResponse(
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
