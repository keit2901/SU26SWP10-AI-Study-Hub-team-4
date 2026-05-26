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

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri())
        {
            Content = new StringContent(
                JsonSerializer.Serialize(BuildPayload(request), JsonOptions),
                Encoding.UTF8,
                MediaTypeNames.Application.Json)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Groq chat completion failed with HTTP {StatusCode}.", (int)response.StatusCode);
            throw new AiChatProviderException(
                "groq_http_error",
                $"Groq chat completion failed with HTTP {(int)response.StatusCode}.");
        }

        try
        {
            var completion = JsonSerializer.Deserialize<GroqChatCompletionResponse>(body, JsonOptions);
            var answer = completion?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(answer))
            {
                throw new AiChatProviderException("groq_empty_response", "Groq returned an empty answer.");
            }

            return answer;
        }
        catch (JsonException ex)
        {
            throw new AiChatProviderException("groq_invalid_response", "Groq returned an invalid response.", ex);
        }
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
        return new
        {
            model = string.IsNullOrWhiteSpace(_options.Model) ? "llama-3.1-8b-instant" : _options.Model,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt },
            },
            temperature = _options.Temperature,
            max_tokens = _options.MaxTokens > 0 ? _options.MaxTokens : 1024,
        };
    }

    private sealed record GroqChatCompletionResponse(IReadOnlyList<GroqChoice>? Choices);

    private sealed record GroqChoice(GroqMessage? Message);

    private sealed record GroqMessage(string? Content);
}
