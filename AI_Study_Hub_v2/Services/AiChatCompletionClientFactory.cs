using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class AiChatCompletionClientFactory : IAiChatCompletionClientFactory
{
    private static readonly HashSet<string> GeminiPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "gemini",
        "gemini-",
    };

    private readonly GroqChatCompletionClient _groqClient;
    private readonly GeminiChatCompletionClient _geminiClient;
    private readonly GeminiOptions _geminiOptions;
    private readonly ILogger<AiChatCompletionClientFactory> _logger;

    public AiChatCompletionClientFactory(
        GroqChatCompletionClient groqClient,
        GeminiChatCompletionClient geminiClient,
        IOptions<GeminiOptions> geminiOptions,
        ILogger<AiChatCompletionClientFactory> logger)
    {
        _groqClient = groqClient;
        _geminiClient = geminiClient;
        _geminiOptions = geminiOptions.Value;
        _logger = logger;
    }

    public IAiChatCompletionClient GetClient(string? modelName)
    {
        if (IsGeminiModel(modelName))
        {
            if (string.IsNullOrWhiteSpace(_geminiOptions.ApiKey))
            {
                _logger.LogWarning(
                    "Gemini model '{Model}' requested but Gemini API key is not configured. Falling back to Groq.",
                    modelName);
                return _groqClient;
            }
            return _geminiClient;
        }
        return _groqClient;
    }

    public string GetProviderName(string? modelName)
    {
        return IsGeminiModel(modelName) ? "gemini" : "groq";
    }

    private static bool IsGeminiModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return false;
        return GeminiPrefixes.Any(p => modelName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
