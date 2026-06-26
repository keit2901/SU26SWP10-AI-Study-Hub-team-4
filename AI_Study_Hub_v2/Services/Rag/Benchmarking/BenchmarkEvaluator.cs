using System.Diagnostics;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

public sealed class BenchmarkEvaluator
{
    private readonly ILogger<BenchmarkEvaluator> _logger;

    public BenchmarkEvaluator(ILogger<BenchmarkEvaluator> logger)
    {
        _logger = logger;
    }

    public BenchmarkResult Evaluate(
        string modelName,
        string provider,
        IReadOnlyList<BenchmarkResponse> responses,
        IReadOnlyList<BenchmarkCategoryScore>? externalCategoryScores = null)
    {
        var categoryGroups = responses.GroupBy(r => r.Question.Category);

        var categoryScores = new List<BenchmarkCategoryScore>();
        foreach (var group in categoryGroups)
        {
            var total = group.Count();
            var passed = group.Count(r => IsPassing(r, group.Key));
            var avg = group.Average(r => ScoreResponse(r, group.Key));
            categoryScores.Add(new BenchmarkCategoryScore(group.Key, total, passed, avg));
        }

        var groundedResponses = responses.Where(r => r.Question.HasSourceDocuments).ToList();
        var citationAccuracy = groundedResponses.Count == 0
            ? 0
            : groundedResponses.Average(r => ScoreCitationAccuracy(r));

        var hallucinationRate = responses.Count == 0
            ? 0
            : (double)responses.Count(r => HasHallucination(r)) / responses.Count;

        var refusalQ = responses.Where(r => r.Question.ExpectedBehavior == "refusal").ToList();
        var refusalAccuracy = refusalQ.Count == 0
            ? 0
            : (double)refusalQ.Count(r => IsCorrectRefusal(r)) / refusalQ.Count;

        var diagramQ = responses.Where(r => r.Question.Category is BenchmarkCategory.DiagramExplanation).ToList();
        var diagramAccuracy = diagramQ.Count == 0
            ? 0
            : diagramQ.Average(r => ScoreDiagramResponse(r));

        var tutoringQ = responses.Where(r => r.Answer is not null).ToList();
        var tutoringQuality = tutoringQ.Count == 0
            ? 0
            : tutoringQ.Average(r => ScoreTutoringQuality(r));

        var latencies = responses.Where(r => r.DurationMs > 0).Select(r => r.DurationMs).OrderBy(x => x).ToList();
        var p50 = latencies.Count > 0 ? latencies[(int)(latencies.Count * 0.50)] : 0;
        var p95 = latencies.Count > 0 ? latencies[(int)(latencies.Count * 0.95)] : 0;

        var overallScore = (
            citationAccuracy * 0.25 +
            (1.0 - hallucinationRate) * 0.20 +
            refusalAccuracy * 0.15 +
            tutoringQuality * 0.10 +
            diagramAccuracy * 0.15 +
            Math.Min(1.0, (60000.0 - p50) / 60000.0) * 0.15
        );

        return new BenchmarkResult(
            modelName,
            provider,
            DateTimeOffset.UtcNow,
            categoryScores,
            citationAccuracy,
            hallucinationRate,
            refusalAccuracy,
            tutoringQuality,
            diagramAccuracy,
            overallScore,
            p50,
            p95,
            responses);
    }

    private static bool IsPassing(BenchmarkResponse r, BenchmarkCategory category)
    {
        if (r.Error is not null || r.Answer is null)
        {
            return false;
        }

        if (r.Question.ExpectedBehavior == "refusal")
        {
            return IsCorrectRefusal(r);
        }

        if (r.Question.ReferenceAnswer is not null)
        {
            return r.Answer.Contains(r.Question.ReferenceAnswer[..Math.Min(30, r.Question.ReferenceAnswer.Length)],
                StringComparison.OrdinalIgnoreCase);
        }

        return r.Answer.Length >= 20;
    }

    private static double ScoreResponse(BenchmarkResponse r, BenchmarkCategory category)
    {
        if (r.Error is not null || r.Answer is null)
        {
            return 0.0;
        }

        var score = 0.5;

        if (r.Sources is { Count: > 0 } && r.Question.ExpectedBehavior == "citation")
        {
            score += 0.2;
        }

        if (r.Answer.Length >= 50 && r.Answer.Length <= 2000)
        {
            score += 0.15;
        }

        if (r.DurationMs > 0 && r.DurationMs < 30000)
        {
            score += 0.15;
        }

        return Math.Min(1.0, score);
    }

    private static double ScoreCitationAccuracy(BenchmarkResponse r)
    {
        if (r.Answer is null || r.Sources is null || r.Sources.Count == 0)
        {
            return 0.0;
        }

        var hasUsedSources = r.Sources.Any(s => r.Answer.Contains($"[{s.Label}]"));
        return hasUsedSources ? 1.0 : 0.5;
    }

    private static bool HasHallucination(BenchmarkResponse r)
    {
        if (r.Answer is null)
        {
            return false;
        }

        if (r.Question.ExpectedBehavior == "refusal" && !IsCorrectRefusal(r))
        {
            return r.Answer.Length > 50;
        }

        return false;
    }

    private static bool IsCorrectRefusal(BenchmarkResponse r)
    {
        if (r.Answer is null)
        {
            return false;
        }

        var refusalPhrases = new[]
        {
            "do not contain enough information",
            "does not contain enough information",
            "indexed documents do not",
            "my training data does not cover",
            "not covered in the provided",
            "cannot find",
            "not included in the",
        };

        return refusalPhrases.Any(p => r.Answer.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static double ScoreDiagramResponse(BenchmarkResponse r)
    {
        if (r.Answer is null)
        {
            return 0.0;
        }

        var score = 0.3;

        var typeKeywords = new[] { "diagram", "flowchart", "uml", "erd", "architecture", "chart", "graph", "sequence" };
        if (typeKeywords.Any(k => r.Answer.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.25;
        }

        if (r.Answer.Contains("component", StringComparison.OrdinalIgnoreCase) ||
            r.Answer.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
            r.Answer.Contains("relationship", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.25;
        }

        if (r.Answer.Length >= 40)
        {
            score += 0.2;
        }

        return Math.Min(1.0, score);
    }

    private static double ScoreTutoringQuality(BenchmarkResponse r)
    {
        if (r.Answer is null)
        {
            return 0.0;
        }

        var score = 0.3;

        if (r.Answer.Contains("example", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.2;
        }

        var structureMarkers = new[] { "- **", "* **", "1. ", "2. ", "first", "second" };
        if (structureMarkers.Any(m => r.Answer.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.25;
        }

        var explanationMarkers = new[] { " means ", " refers to ", "is used to", "can be thought of", "imagine " };
        if (explanationMarkers.Any(m => r.Answer.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.25;
        }

        return Math.Min(1.0, score);
    }
}
