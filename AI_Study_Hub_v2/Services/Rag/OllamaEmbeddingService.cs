using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag;

public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaEmbeddingService> _logger;

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
        var prompt = text ?? string.Empty;
        var maxAttempts = Math.Max(1, _options.MaxRetries);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
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

                return result.Embedding;
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                var delaySeconds = (int)Math.Pow(2, attempt - 1);

                _logger.LogWarning(
                    ex,
                    "Ollama embedding attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelaySeconds}s.",
                    attempt,
                    maxAttempts,
                    delaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        throw new InvalidOperationException("Failed to generate embedding from Ollama.");
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