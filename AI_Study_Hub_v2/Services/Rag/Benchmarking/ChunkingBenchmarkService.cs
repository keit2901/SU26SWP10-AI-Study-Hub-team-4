using System.Text.RegularExpressions;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

public sealed class ChunkingBenchmarkService
{
    private static readonly Regex CollapseWhitespace = new(@"\s+", RegexOptions.Compiled);
    private readonly ChunkingService _semanticChunking;
    private readonly FixedSizeChunkingService _fixedChunking;
    private readonly SemanticV2ChunkingService _semanticV2Chunking;
    private readonly ITokenEstimator _tokens;
    private readonly IEmbeddingService _embeddingService;
    private readonly RagOptions _options;

    public ChunkingBenchmarkService(
        ChunkingService semanticChunking,
        FixedSizeChunkingService fixedChunking,
        SemanticV2ChunkingService semanticV2Chunking,
        ITokenEstimator tokens,
        IEmbeddingService embeddingService,
        IOptions<RagOptions> options)
    {
        _semanticChunking = semanticChunking;
        _fixedChunking = fixedChunking;
        _semanticV2Chunking = semanticV2Chunking;
        _tokens = tokens;
        _embeddingService = embeddingService;
        _options = options.Value;
    }

    public async Task<ChunkingBenchmarkComparisonResult> RunAsync(int? topK = null, IReadOnlyList<ChunkingBenchmarkScenario>? scenarios = null, CancellationToken cancellationToken = default)
    {
        var effectiveTopK = ResolveTopK(topK);
        var dataset = scenarios ?? ChunkingBenchmarkDataset.All;
        var results = new List<ChunkingBenchmarkScenarioResult>(dataset.Count);
        foreach (var scenario in dataset)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fixedResult = await EvaluateStrategyAsync("fixed-v1", scenario, effectiveTopK, _fixedChunking, cancellationToken);
            var semanticResult = await EvaluateStrategyAsync("semantic-v1", scenario, effectiveTopK, _semanticChunking, cancellationToken);
            var semanticV2Result = await EvaluateStrategyAsync("semantic-v2", scenario, effectiveTopK, _semanticV2Chunking, cancellationToken);
            results.Add(new ChunkingBenchmarkScenarioResult(scenario.Id, scenario.Title, fixedResult, semanticResult, semanticV2Result));
        }

        var fixedSummary = Summarize("fixed-v1", results.Select(result => result.Fixed).ToArray());
        var semanticSummary = Summarize("semantic-v1", results.Select(result => result.Semantic).ToArray());
        var semanticV2Summary = Summarize("semantic-v2", results.Select(result => result.SemanticV2!).ToArray());
        return new ChunkingBenchmarkComparisonResult(DateTimeOffset.UtcNow, effectiveTopK, results, fixedSummary, semanticSummary, semanticV2Summary, EvaluateGates(semanticSummary, semanticV2Summary, results));
    }

    private async Task<ChunkingStrategyBenchmarkResult> EvaluateStrategyAsync(string strategy, ChunkingBenchmarkScenario scenario, int topK, IChunkingService chunker, CancellationToken cancellationToken)
    {
        var measured = ChunkingBenchmarkMetrics.Measure(chunker, Guid.NewGuid(), scenario.Pages);
        var chunks = measured.Chunks;
        var chunkEmbeddings = new List<(DocumentChunkDraft Chunk, float[] Embedding)>(chunks.Count);
        foreach (var chunk in chunks)
        {
            chunkEmbeddings.Add((chunk, await _embeddingService.GenerateEmbeddingAsync(chunk.Content, cancellationToken)));
        }

        var cases = new List<ChunkingBenchmarkCaseResult>(scenario.Cases.Count);
        foreach (var @case in scenario.Cases)
        {
            var query = await _embeddingService.GenerateEmbeddingAsync(@case.Query, cancellationToken);
            var ranked = chunkEmbeddings.Select(entry => new { entry.Chunk, Distance = CosineDistance(entry.Embedding, query) })
                .OrderBy(entry => entry.Distance).ThenBy(entry => entry.Chunk.ChunkIndex).ToList();
            var relevance = ranked.Select((entry, index) => new { Rank = index + 1, Relevant = MatchesExpectedPhrase(entry.Chunk.Content, @case.ExpectedPhrases) }).ToArray();
            var firstRank = relevance
                .Where(item => item.Relevant).Select(item => (int?)item.Rank).FirstOrDefault();
            var reciprocal = firstRank is null ? 0d : 1d / firstRank.Value;
            var dcg = relevance.Take(5).Where(item => item.Relevant).Sum(item => 1d / Math.Log2(item.Rank + 1d));
            var idealRelevant = Math.Min(5, relevance.Count(item => item.Relevant));
            var idcg = Enumerable.Range(1, idealRelevant).Sum(rank => 1d / Math.Log2(rank + 1d));
            var ndcg = idcg == 0d ? 0d : dcg / idcg;
            cases.Add(new ChunkingBenchmarkCaseResult(@case.Id, @case.Query, firstRank.HasValue && firstRank.Value <= topK, reciprocal,
                ranked.Take(topK).Select((entry, index) => new ChunkingBenchmarkHit(index + 1, entry.Chunk.ChunkIndex, BuildPreview(entry.Chunk.Content))).ToArray(), firstRank, ndcg));
        }

        var intrinsic = ChunkingBenchmarkMetrics.Analyze(chunks, scenario.Pages, _tokens, _options, measured.LatencyMs);
        var successfulRanks = cases.Where(item => item.FirstRelevantRank.HasValue).Select(item => (double)item.FirstRelevantRank!.Value).ToArray();
        return new ChunkingStrategyBenchmarkResult(
            strategy, chunks.Count, chunks.Count == 0 ? 0d : chunks.Average(chunk => chunk.Content.Length), chunks.Count(chunk => chunk.Content.Length < _options.MinChunkChars),
            cases.Count == 0 ? 0d : cases.Average(item => item.HitAtTopK ? 1d : 0d), cases.Count == 0 ? 0d : cases.Average(item => item.ReciprocalRank), cases,
            RecallAt1: Recall(cases, 1), RecallAt3: Recall(cases, 3), RecallAt5: Recall(cases, 5), NdcgAt5: cases.Count == 0 ? 0d : cases.Average(item => item.NdcgAt5),
            MeanFirstRelevantRank: successfulRanks.Length == 0 ? null : successfulRanks.Average(),
            ZeroHitQueryCount: cases.Count(item => item.FirstRelevantRank is null), Intrinsic: intrinsic);
    }

    private static double Recall(IReadOnlyList<ChunkingBenchmarkCaseResult> cases, int cutoff) =>
        cases.Count == 0 ? 0d : cases.Average(item => item.FirstRelevantRank.HasValue && item.FirstRelevantRank.Value <= cutoff ? 1d : 0d);

    private int ResolveTopK(int? topK) => Math.Clamp(topK.GetValueOrDefault(_options.DefaultTopK > 0 ? _options.DefaultTopK : 5), 1, _options.MaxTopK > 0 ? _options.MaxTopK : 10);
    private static bool MatchesExpectedPhrase(string content, IReadOnlyList<string> phrases) => phrases.Any(phrase => Normalize(content).Contains(Normalize(phrase), StringComparison.Ordinal));
    private static string Normalize(string text) => CollapseWhitespace.Replace(text.Trim(), " ").ToLowerInvariant();
    private static string BuildPreview(string content) { var value = CollapseWhitespace.Replace(content.Trim(), " "); return value.Length <= 140 ? value : value[..140].TrimEnd() + "..."; }

    private static double CosineDistance(float[] left, float[] right)
    {
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (var index = 0; index < left.Length; index++) { dot += left[index] * right[index]; leftNorm += left[index] * left[index]; rightNorm += right[index] * right[index]; }
        return leftNorm <= 0 || rightNorm <= 0 ? 1d : 1d - dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    private static ChunkingBenchmarkSummary Summarize(string strategy, IReadOnlyList<ChunkingStrategyBenchmarkResult> items)
    {
        var metrics = items.Select(item => item.Intrinsic!).ToArray();
        var successfulRanks = items.Select(item => item.MeanFirstRelevantRank).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return new ChunkingBenchmarkSummary(strategy, items.Sum(item => item.ChunkCount), items.Count == 0 ? 0d : items.Average(item => item.AverageChunkChars), items.Sum(item => item.TinyChunkCount),
            items.Count == 0 ? 0d : items.Average(item => item.RecallAtK), items.Count == 0 ? 0d : items.Average(item => item.MeanReciprocalRank),
            items.Count == 0 ? 0d : items.Average(item => item.RecallAt1), items.Count == 0 ? 0d : items.Average(item => item.RecallAt3), items.Count == 0 ? 0d : items.Average(item => item.RecallAt5), items.Count == 0 ? 0d : items.Average(item => item.NdcgAt5),
            successfulRanks.Length == 0 ? null : successfulRanks.Average(), items.Sum(item => item.ZeroHitQueryCount), Aggregate(metrics));
    }

    private static ChunkingIntrinsicMetrics Aggregate(IReadOnlyList<ChunkingIntrinsicMetrics> metrics) => new(
        metrics.Sum(item => item.EmbeddingCalls), metrics.Sum(item => item.EstimatedInputTokenVolume), Samples(metrics).FirstOrDefault(), Samples(metrics).Length == 0 ? 0d : Samples(metrics).Average(),
        Percentile(Samples(metrics), .50), Percentile(Samples(metrics), .90), Percentile(Samples(metrics), .95), Samples(metrics).LastOrDefault(),
        metrics.Sum(item => item.BelowMinimumCount), metrics.Sum(item => item.AboveMaximumCount), metrics.Count == 0 ? 0d : metrics.Average(item => item.TinyOrphanRate), metrics.Count == 0 ? 0d : metrics.Average(item => item.AdjacentOverlapEstimatedTokens), metrics.Count == 0 ? 0d : metrics.Max(item => item.MaxAdjacentLineOverlapEstimatedTokens), metrics.Count == 0 ? 0d : metrics.Average(item => item.EstimatedTokenExpansionRatio),
        null, null, null, null, null, metrics.Sum(item => item.ChunkingLatencyMs), metrics.SelectMany(item => item.UnavailableMetrics).Distinct(StringComparer.Ordinal).ToArray(), Samples(metrics));

    private static int[] Samples(IReadOnlyList<ChunkingIntrinsicMetrics> metrics) => metrics.SelectMany(item => item.EstimatedTokenSamples).Order().ToArray();
    private static double Percentile(IReadOnlyList<int> samples, double percentile) => samples.Count == 0 ? 0d : samples[Math.Clamp((int)Math.Ceiling(samples.Count * percentile) - 1, 0, samples.Count - 1)];

    private SemanticV2ReleaseGates EvaluateGates(ChunkingBenchmarkSummary semantic, ChunkingBenchmarkSummary v2, IReadOnlyList<ChunkingBenchmarkScenarioResult> scenarios)
    {
        var metrics = v2.Intrinsic!;
        var inRangeRate = v2.ChunkCount == 0 ? 0d : (v2.ChunkCount - metrics.BelowMinimumCount - metrics.AboveMaximumCount) / (double)v2.ChunkCount;
        var zeroAbove = metrics.AboveMaximumCount == 0;
        var inRange = inRangeRate >= .90d;
        var p50 = metrics.P50EstimatedTokens is >= 112d and <= 168d;
        var overlap = metrics.MaxAdjacentLineOverlapEstimatedTokens <= _options.SemanticOverlapTokens;
        var duplication = false; // Source-atom provenance is unavailable, so true duplication cannot be asserted.
        var count = v2.ChunkCount <= semantic.ChunkCount * 1.35d;
        var volume = metrics.EstimatedInputTokenVolume <= semantic.Intrinsic!.EstimatedInputTokenVolume * 1.25d;
        var recall = v2.RecallAt5 >= semantic.RecallAt5;
        var mrr = v2.MeanReciprocalRank >= semantic.MeanReciprocalRank - .02d;
        const bool requiredProvenanceAndRealEmbeddingAvailable = false;
        var scenarioRecall = scenarios.Select(scenario => Diagnostic(scenario, recallAt3: false)).ToArray();
        var listTableRecall = scenarios.Where(scenario => scenario.ScenarioId is "MIXED-LIST" or "TABLE-LIKE")
            .Select(scenario => Diagnostic(scenario, recallAt3: true)).ToArray();
        return new SemanticV2ReleaseGates(zeroAbove, inRange, p50, overlap, duplication, count, volume, recall, mrr, inRangeRate, requiredProvenanceAndRealEmbeddingAvailable, false, scenarioRecall, listTableRecall);
    }

    private static ScenarioRecallDiagnostic Diagnostic(ChunkingBenchmarkScenarioResult scenario, bool recallAt3)
    {
        var baseline = recallAt3 ? scenario.Semantic.RecallAt3 : scenario.Semantic.RecallAt5;
        var v2 = recallAt3 ? scenario.SemanticV2!.RecallAt3 : scenario.SemanticV2!.RecallAt5;
        var delta = v2 - baseline;
        var passed = recallAt3
            ? delta >= .10d || (baseline == 1d && v2 == 1d)
            : delta >= -.05d;
        return new ScenarioRecallDiagnostic(scenario.ScenarioId, scenario.Title, baseline, v2, delta, passed);
    }
}
