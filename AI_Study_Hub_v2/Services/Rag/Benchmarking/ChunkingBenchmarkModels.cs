namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

public sealed record ChunkingBenchmarkCase(string Id, string Query, IReadOnlyList<string> ExpectedPhrases);

public sealed record ChunkingBenchmarkScenario(
    string Id,
    string Title,
    IReadOnlyList<ExtractedPage> Pages,
    IReadOnlyList<ChunkingBenchmarkCase> Cases);

public sealed record ChunkingBenchmarkHit(int Rank, int ChunkIndex, string ContentPreview);

// New optional positional properties preserve existing JSON fields and callers using the original constructor.
public sealed record ChunkingBenchmarkCaseResult(
    string CaseId,
    string Query,
    bool HitAtTopK,
    double ReciprocalRank,
    IReadOnlyList<ChunkingBenchmarkHit> TopHits,
    int? FirstRelevantRank = null,
    double NdcgAt5 = 0d);

public sealed record ChunkingIntrinsicMetrics(
    int EmbeddingCalls,
    long EstimatedInputTokenVolume,
    int MinEstimatedTokens,
    double MeanEstimatedTokens,
    double P50EstimatedTokens,
    double P90EstimatedTokens,
    double P95EstimatedTokens,
    int MaxEstimatedTokens,
    int BelowMinimumCount,
    int AboveMaximumCount,
    double TinyOrphanRate,
    double AdjacentOverlapEstimatedTokens,
    double MaxAdjacentLineOverlapEstimatedTokens,
    double EstimatedTokenExpansionRatio,
    double? HeadingAssociationRetention,
    double? SourceTextCoverage,
    int? ListAtomSplitCount,
    int? TableAtomSplitCount,
    int? CrossPageOrSectionCount,
    double ChunkingLatencyMs,
    IReadOnlyList<string> UnavailableMetrics,
    IReadOnlyList<int> EstimatedTokenSamples);

public sealed record ChunkingStrategyBenchmarkResult(
    string Strategy,
    int ChunkCount,
    double AverageChunkChars,
    int TinyChunkCount,
    double RecallAtK,
    double MeanReciprocalRank,
    IReadOnlyList<ChunkingBenchmarkCaseResult> CaseResults,
    double RecallAt1 = 0d,
    double RecallAt3 = 0d,
    double RecallAt5 = 0d,
    double NdcgAt5 = 0d,
    double? MeanFirstRelevantRank = null,
    int ZeroHitQueryCount = 0,
    ChunkingIntrinsicMetrics? Intrinsic = null);

public sealed record ChunkingBenchmarkScenarioResult(
    string ScenarioId,
    string Title,
    ChunkingStrategyBenchmarkResult Fixed,
    ChunkingStrategyBenchmarkResult Semantic,
    ChunkingStrategyBenchmarkResult? SemanticV2 = null);

public sealed record ChunkingBenchmarkSummary(
    string Strategy,
    int ChunkCount,
    double AverageChunkChars,
    int TinyChunkCount,
    double RecallAtK,
    double MeanReciprocalRank,
    double RecallAt1 = 0d,
    double RecallAt3 = 0d,
    double RecallAt5 = 0d,
    double NdcgAt5 = 0d,
    double? MeanFirstRelevantRank = null,
    int ZeroHitQueryCount = 0,
    ChunkingIntrinsicMetrics? Intrinsic = null);

public sealed record SemanticV2ReleaseGates(
    bool ZeroAboveMax,
    bool AtLeastNinetyPercentInRange,
    bool P50WithinTargetBand,
    bool EligibleOverlapWithinLimit,
    bool DuplicationWithinLimit,
    bool ChunkCountWithinBaselineRatio,
    bool TokenVolumeWithinBaselineRatio,
    bool RecallAt5NonRegression,
    bool MrrWithinAllowedDegradation,
    double InRangeRate,
    bool RequiredProvenanceAndRealEmbeddingAvailable,
    bool Passed,
    IReadOnlyList<ScenarioRecallDiagnostic>? ScenarioRecallAt5Diagnostics = null,
    IReadOnlyList<ScenarioRecallDiagnostic>? ListTableRecallAt3Diagnostics = null)
{
    // Compatibility alias: exceptions are not classified, so this is the same canonical range rate.
    [Obsolete("Use InRangeRate; exceptions are not classified separately.")]
    public double NonExceptionInRangeRate => InRangeRate;
}

public sealed record ScenarioRecallDiagnostic(
    string ScenarioId,
    string ScenarioTitle,
    double SemanticV1Recall,
    double SemanticV2Recall,
    double Delta,
    bool Passed);

public sealed record ChunkingBenchmarkComparisonResult(
    DateTimeOffset RunAt,
    int TopK,
    IReadOnlyList<ChunkingBenchmarkScenarioResult> Scenarios,
    ChunkingBenchmarkSummary Fixed,
    ChunkingBenchmarkSummary Semantic,
    ChunkingBenchmarkSummary? SemanticV2 = null,
    SemanticV2ReleaseGates? SemanticV2Gates = null);
