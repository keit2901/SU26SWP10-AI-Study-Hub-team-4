using AI_Study_Hub_v2.Options;

namespace AI_Study_Hub_v2.Services;

public static class AiChatModelCatalog
{
    public static IReadOnlyList<string> GetAvailableModels(
        AiChatOptions aiChatOptions,
        GroqOptions groqOptions,
        GeminiOptions geminiOptions)
    {
        var models = new List<string>();
        var defaultProviderIsGemini = string.Equals(
            aiChatOptions.DefaultProvider,
            "gemini",
            StringComparison.OrdinalIgnoreCase);

        AddModel(defaultProviderIsGemini ? geminiOptions.Model : groqOptions.Model);
        if (defaultProviderIsGemini)
        {
            AddModel(groqOptions.Model);
        }
        else if (!string.IsNullOrWhiteSpace(geminiOptions.ApiKey))
        {
            AddModel(geminiOptions.Model);
        }

        return models;

        void AddModel(string? model)
        {
            if (!string.IsNullOrWhiteSpace(model)
                && !models.Contains(model, StringComparer.OrdinalIgnoreCase))
            {
                models.Add(model.Trim());
            }
        }
    }
}
