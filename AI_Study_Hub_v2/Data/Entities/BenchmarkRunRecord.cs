namespace AI_Study_Hub_v2.Data.Entities;

public sealed class BenchmarkRunRecord
{
    public Guid Id { get; set; }

    public string ModelName { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public DateTimeOffset RunAt { get; set; }

    public double OverallScore { get; set; }

    public double CitationAccuracy { get; set; }

    public double HallucinationRate { get; set; }

    public double RefusalAccuracy { get; set; }

    public double TutoringQuality { get; set; }

    public double DiagramAccuracy { get; set; }

    public long P50LatencyMs { get; set; }

    public long P95LatencyMs { get; set; }

    public int TotalQuestions { get; set; }

    public int PassedQuestions { get; set; }

    public int FailedQuestions { get; set; }

    public bool IsAutomated { get; set; }

    public bool AlertTriggered { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}
