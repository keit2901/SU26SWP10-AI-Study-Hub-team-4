namespace AI_Study_Hub_v2.Options;

public sealed class AiChatOptions
{
    public const string SectionName = "AiChat";

    public string DefaultProvider { get; set; } = "groq";

    public static bool IsSupportedProvider(string? provider)
        => string.Equals(provider, "groq", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "gemini", StringComparison.OrdinalIgnoreCase);

    public static bool HasValidDefaultProviderConfiguration(AiChatOptions options, GeminiOptions geminiOptions)
        => IsSupportedProvider(options.DefaultProvider)
           && (!string.Equals(options.DefaultProvider, "gemini", StringComparison.OrdinalIgnoreCase)
               || !string.IsNullOrWhiteSpace(geminiOptions.ApiKey));
}
