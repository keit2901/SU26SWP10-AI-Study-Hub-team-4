namespace AI_Study_Hub_v2.Options;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-2.5-flash";

    public double Temperature { get; set; } = 0.2;

    public int MaxTokens { get; set; } = 4096;
}
