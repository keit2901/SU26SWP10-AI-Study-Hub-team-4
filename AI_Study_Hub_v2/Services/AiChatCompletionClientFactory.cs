using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

public sealed class AiChatCompletionClientFactory : IAiChatCompletionClientFactory
{
    private readonly GroqChatCompletionClient _groqClient;
    private readonly GeminiChatCompletionClient _geminiClient;
    private readonly GroqOptions _groqOptions;
    private readonly GeminiOptions _geminiOptions;

    public AiChatCompletionClientFactory(
        GroqChatCompletionClient groqClient,
        GeminiChatCompletionClient geminiClient,
        IOptions<GroqOptions> groqOptions,
        IOptions<GeminiOptions> geminiOptions)
    {
        _groqClient = groqClient;
        _geminiClient = geminiClient;
        _groqOptions = groqOptions.Value;
        _geminiOptions = geminiOptions.Value;
    }

    public IAiChatCompletionClient GetClient(string? modelName)
    {
        var resolvedModel = ResolveModelName(modelName);
        if (string.Equals(resolvedModel, _groqOptions.Model, StringComparison.OrdinalIgnoreCase))
        {
            return _groqClient;
        }

        return _geminiClient;
    }

    public string GetProviderName(string? modelName)
    {
        var resolvedModel = ResolveModelName(modelName);
        return string.Equals(resolvedModel, _groqOptions.Model, StringComparison.OrdinalIgnoreCase)
            ? "groq"
            : "gemini";
    }

    private string ResolveModelName(string? modelName)
    {
        var resolvedModel = string.IsNullOrWhiteSpace(modelName) ? _groqOptions.Model : modelName.Trim();
        if (string.Equals(resolvedModel, _groqOptions.Model, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resolvedModel, _geminiOptions.Model, StringComparison.OrdinalIgnoreCase))
        {
            return resolvedModel;
        }

        throw new AiChatModelException(resolvedModel);
    }
}
