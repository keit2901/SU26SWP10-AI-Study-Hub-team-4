namespace AI_Study_Hub_v2.Options;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";
    public const string OllamaProvider = "Ollama";
    public const string FakeProvider = "Fake";

    public string Provider { get; set; } = OllamaProvider;

    public bool UsesOllama => string.Equals(Provider, OllamaProvider, StringComparison.OrdinalIgnoreCase);

    public bool UsesFake => string.Equals(Provider, FakeProvider, StringComparison.OrdinalIgnoreCase);

    public static bool IsSupported(EmbeddingOptions options) =>
        options is not null && (options.UsesOllama || options.UsesFake);
}
