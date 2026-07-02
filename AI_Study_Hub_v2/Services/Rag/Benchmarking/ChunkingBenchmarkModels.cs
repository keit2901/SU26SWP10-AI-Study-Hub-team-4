namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

public sealed record ChunkingBenchmarkCase(
    string Id,
    string Query,
    IReadOnlyList<string> ExpectedPhrases);

public sealed record ChunkingBenchmarkScenario(
    string Id,
    string Title,
    IReadOnlyList<ExtractedPage> Pages,
    IReadOnlyList<ChunkingBenchmarkCase> Cases);

public sealed record ChunkingBenchmarkHit(
    int Rank,
    int ChunkIndex,
    string ContentPreview);

public sealed record ChunkingBenchmarkCaseResult(
    string CaseId,
    string Query,
    bool HitAtTopK,
    double ReciprocalRank,
    IReadOnlyList<ChunkingBenchmarkHit> TopHits);

public sealed record ChunkingStrategyBenchmarkResult(
    string Strategy,
    int ChunkCount,
    double AverageChunkChars,
    int TinyChunkCount,
    double RecallAtK,
    double MeanReciprocalRank,
    IReadOnlyList<ChunkingBenchmarkCaseResult> CaseResults);

public sealed record ChunkingBenchmarkScenarioResult(
    string ScenarioId,
    string Title,
    ChunkingStrategyBenchmarkResult Fixed,
    ChunkingStrategyBenchmarkResult Semantic);

public sealed record ChunkingBenchmarkSummary(
    string Strategy,
    int ChunkCount,
    double AverageChunkChars,
    int TinyChunkCount,
    double RecallAtK,
    double MeanReciprocalRank);

public sealed record ChunkingBenchmarkComparisonResult(
    DateTimeOffset RunAt,
    int TopK,
    IReadOnlyList<ChunkingBenchmarkScenarioResult> Scenarios,
    ChunkingBenchmarkSummary Fixed,
    ChunkingBenchmarkSummary Semantic);
