namespace AI_Study_Hub_v2.Dtos;

public sealed record BenchmarkHistoryItemDto(
    Guid Id,
    string ModelName,
    string Provider,
    DateTimeOffset RunAt,
    double OverallScore,
    double CitationAccuracy,
    double HallucinationRate,
    double RefusalAccuracy,
    double TutoringQuality,
    double DiagramAccuracy,
    long P50LatencyMs,
    long P95LatencyMs,
    int TotalQuestions,
    int PassedQuestions,
    int FailedQuestions,
    bool IsAutomated,
    bool AlertTriggered);
