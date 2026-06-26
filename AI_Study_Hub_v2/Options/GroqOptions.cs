namespace AI_Study_Hub_v2.Options;

public sealed class GroqOptions
{
    public const string SectionName = "Groq";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "llama-3.3-70b-versatile";

    public string Endpoint { get; set; } = "https://api.groq.com/openai/v1";

    public double Temperature { get; set; } = 0.2;

    public int MaxTokens { get; set; } = 4096;

    public bool UseLocalDemoFallback { get; set; }

    public string VisionModel { get; set; } = "meta-llama/llama-4-scout-17b-16e-instruct";

    public int MaxImagesPerDocument { get; set; } = 50;

    public int MaxImageSizeMb { get; set; } = 3;

    public bool SkipImagesWhenLimitExceeded { get; set; } = true;
}
