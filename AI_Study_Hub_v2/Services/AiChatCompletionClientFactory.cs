using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class AiChatCompletionClientFactory : IAiChatCompletionClientFactory
{
    private readonly GroqChatCompletionClient _groqClient;
    private readonly GeminiChatCompletionClient _geminiClient;
    private readonly GroqOptions _groqOptions;
    private readonly GeminiOptions _geminiOptions;
    private readonly AiChatOptions _aiChatOptions;

    public AiChatCompletionClientFactory(
        GroqChatCompletionClient groqClient,
        GeminiChatCompletionClient geminiClient,
        IOptions<GroqOptions> groqOptions,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<AiChatOptions> aiChatOptions)
    {
        _groqClient = groqClient;
        _geminiClient = geminiClient;
        _groqOptions = groqOptions.Value;
        _geminiOptions = geminiOptions.Value;
        _aiChatOptions = aiChatOptions.Value;
    }

    public IAiChatCompletionClient GetClient(string? modelName)
    {
        return ResolveProvider(modelName) == "groq" ? _groqClient : _geminiClient;
    }

    public string GetProviderName(string? modelName)
    {
        return ResolveProvider(modelName);
    }

    private string ResolveProvider(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return ResolveDefaultProvider();
        }

        var resolvedModel = modelName.Trim();
        if (string.Equals(resolvedModel, _groqOptions.Model, StringComparison.OrdinalIgnoreCase))
        {
            return "groq";
        }

        if (string.Equals(resolvedModel, _geminiOptions.Model, StringComparison.OrdinalIgnoreCase))
        {
            return "gemini";
        }

        throw new AiChatModelException(resolvedModel);
    }

    private string ResolveDefaultProvider()
    {
        if (string.Equals(_aiChatOptions.DefaultProvider, "groq", StringComparison.OrdinalIgnoreCase))
        {
            return "groq";
        }

        if (string.Equals(_aiChatOptions.DefaultProvider, "gemini", StringComparison.OrdinalIgnoreCase))
        {
            return "gemini";
        }

        throw new InvalidOperationException("AiChat:DefaultProvider must be either 'groq' or 'gemini'.");
    }
}
