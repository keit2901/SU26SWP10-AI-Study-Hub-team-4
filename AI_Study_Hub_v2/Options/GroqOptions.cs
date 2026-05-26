namespace AI_Study_Hub_v2.Options;

public sealed class GroqOptions
{
    public const string SectionName = "Groq";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "llama-3.1-8b-instant";

    public string Endpoint { get; set; } = "https://api.groq.com/openai/v1";

    public double Temperature { get; set; } = 0.2;

    public int MaxTokens { get; set; } = 1024;
}
