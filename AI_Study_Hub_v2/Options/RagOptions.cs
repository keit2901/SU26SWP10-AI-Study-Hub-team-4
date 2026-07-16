namespace AI_Study_Hub_v2.Options;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public string ChunkingStrategy { get; set; } = "semantic";

    public int ChunkSizeChars { get; set; } = 700;

    public int ChunkOverlapChars { get; set; } = 70;

    public int MinChunkChars { get; set; } = 200;

    public int MaxSectionChars { get; set; } = 700;

    public int DefaultTopK { get; set; } = 5;

    public int MaxTopK { get; set; } = 10;

    public int EmbeddingDimensions { get; set; } = 384;

    public int MaxContextChars { get; set; } = 6000;

    public bool EmbeddingCacheEnabled { get; set; } = true;

    public int EmbeddingCacheMaxEntries { get; set; } = 1000;

    public int EmbeddingCacheTtlMinutes { get; set; } = 30;

    public bool ReRankEnabled { get; set; } = true;

    public int ReRankCandidateCount { get; set; } = 20;

    public int ReRankTopN { get; set; } = 5;

    public bool HybridSearchEnabled { get; set; } = true;

    public double VectorWeight { get; set; } = 0.7d;

    public string SearchMode { get; set; } = "hybrid";

    public bool BenchmarkAutomationEnabled { get; set; } = false;

    public int BenchmarkAutomationIntervalHours { get; set; } = 168;

    public double BenchmarkAlertDropPercent { get; set; } = 10d;

    public int MaxHistoryExchanges { get; set; } = 4;

    public int MaxHistoryChars { get; set; } = 4000;

    public int MaxAssistantAnswerChars { get; set; } = 600;

    public static bool HasValidChatBounds(RagOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.MaxHistoryExchanges is >= 0 and <= 10
            && (options.MaxHistoryChars == 0 || options.MaxHistoryChars is >= 500 and <= 10000)
            && options.MaxAssistantAnswerChars is >= 100 and <= 2000
            && options.MaxContextChars is >= 1000 and <= 20000;
    }
}
