using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

public enum BenchmarkCategory
{
    Definition,
    Comparison,
    Summary,
    DiagramExplanation,
    TableInterpretation,
    MissingInformation,
    MultiSourceRetrieval,
    ArchitectureUnderstanding,
}

public sealed record BenchmarkQuestion(
    string Id,
    BenchmarkCategory Category,
    string Question,
    bool HasSourceDocuments,
    string ExpectedBehavior,   // "citation" | "refusal" | "general_knowledge"
    string? SourceDocumentHint, // document ID or filename hint for setup
    string? ReferenceAnswer);   // gold-standard answer (null for automated rubric scoring)

public sealed record BenchmarkResponse(
    BenchmarkQuestion Question,
    string? Answer,
    IReadOnlyList<AiChatSourceDto>? Sources,
    long DurationMs,
    string? Error);

public sealed record BenchmarkCategoryScore(
    BenchmarkCategory Category,
    int TotalQuestions,
    int Passed,
    double AverageScore);

public sealed record BenchmarkResult(
    string ModelName,
    string Provider,
    DateTimeOffset RunAt,
    IReadOnlyList<BenchmarkCategoryScore> CategoryScores,
    double CitationAccuracy,
    double HallucinationRate,
    double RefusalAccuracy,
    double TutoringQuality,
    double DiagramAccuracy,
    double OverallScore,
    long P50LatencyMs,
    long P95LatencyMs,
    IReadOnlyList<BenchmarkResponse> Responses);

public sealed record BenchmarkConfig(
    string ModelName,
    string? Provider = null,
    int? Count = null,
    Guid? DocumentId = null,
    IReadOnlyList<Guid>? DocumentIds = null);
