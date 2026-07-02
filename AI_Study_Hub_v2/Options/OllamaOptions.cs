public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "all-minilm:l6-v2";
    public int TimeoutSeconds { get; set; } = 30;   // per-request timeout
    public int MaxRetries { get; set; } = 3;
}