using System.Text.RegularExpressions;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

public sealed class ChunkingBenchmarkService
{
    private static readonly Regex CollapseWhitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly ChunkingService _semanticChunking;
    private readonly FixedSizeChunkingService _fixedChunking;
    private readonly IEmbeddingService _embeddingService;
    private readonly RagOptions _options;

    public ChunkingBenchmarkService(
        ChunkingService semanticChunking,
        FixedSizeChunkingService fixedChunking,
        IEmbeddingService embeddingService,
        IOptions<RagOptions> options)
    {
        _semanticChunking = semanticChunking;
        _fixedChunking = fixedChunking;
        _embeddingService = embeddingService;
        _options = options.Value;
    }

    public async Task<ChunkingBenchmarkComparisonResult> RunAsync(
        int? topK = null,
        IReadOnlyList<ChunkingBenchmarkScenario>? scenarios = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTopK = ResolveTopK(topK);
        var dataset = scenarios ?? ChunkingBenchmarkDataset.All;
        var scenarioResults = new List<ChunkingBenchmarkScenarioResult>(dataset.Count);

        foreach (var scenario in dataset)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fixedResult = await EvaluateStrategyAsync(
                "fixed",
                scenario,
                effectiveTopK,
                _fixedChunking,
                cancellationToken);

            var semanticResult = await EvaluateStrategyAsync(
                "semantic",
                scenario,
                effectiveTopK,
                _semanticChunking,
                cancellationToken);

            scenarioResults.Add(new ChunkingBenchmarkScenarioResult(
                scenario.Id,
                scenario.Title,
                fixedResult,
                semanticResult));
        }

        return new ChunkingBenchmarkComparisonResult(
            DateTimeOffset.UtcNow,
            effectiveTopK,
            scenarioResults,
            Summarize("fixed", scenarioResults.Select(result => result.Fixed).ToList()),
            Summarize("semantic", scenarioResults.Select(result => result.Semantic).ToList()));
    }

    private async Task<ChunkingStrategyBenchmarkResult> EvaluateStrategyAsync(
        string strategy,
        ChunkingBenchmarkScenario scenario,
        int topK,
        IChunkingService chunkingService,
        CancellationToken cancellationToken)
    {
        var documentId = Guid.NewGuid();
        var chunks = chunkingService.Chunk(documentId, scenario.Pages);
        var chunkEmbeddings = new List<(DocumentChunkDraft Chunk, float[] Embedding)>(chunks.Count);

        foreach (var chunk in chunks)
        {
            chunkEmbeddings.Add((chunk, await _embeddingService.GenerateEmbeddingAsync(chunk.Content, cancellationToken)));
        }

        var caseResults = new List<ChunkingBenchmarkCaseResult>(scenario.Cases.Count);
        foreach (var @case in scenario.Cases)
        {
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(@case.Query, cancellationToken);
            var ranked = chunkEmbeddings
                .Select(entry => new
                {
                    entry.Chunk,
                    Distance = CosineDistance(entry.Embedding, queryEmbedding)
                })
                .OrderBy(entry => entry.Distance)
                .ThenBy(entry => entry.Chunk.ChunkIndex)
                .ToList();

            var rankedRelevant = ranked
                .Select((entry, index) => new
                {
                    Rank = index + 1,
                    entry.Chunk,
                    IsRelevant = MatchesExpectedPhrase(entry.Chunk.Content, @case.ExpectedPhrases)
                })
                .ToList();

            var firstRelevant = rankedRelevant.FirstOrDefault(entry => entry.IsRelevant);
            var hitAtTopK = rankedRelevant.Take(topK).Any(entry => entry.IsRelevant);
            var reciprocalRank = firstRelevant is null ? 0d : 1d / firstRelevant.Rank;

            caseResults.Add(new ChunkingBenchmarkCaseResult(
                @case.Id,
                @case.Query,
                hitAtTopK,
                reciprocalRank,
                ranked.Take(topK)
                    .Select((entry, index) => new ChunkingBenchmarkHit(
                        index + 1,
                        entry.Chunk.ChunkIndex,
                        BuildPreview(entry.Chunk.Content)))
                    .ToList()));
        }

        return new ChunkingStrategyBenchmarkResult(
            strategy,
            chunks.Count,
            chunks.Count == 0 ? 0d : chunks.Average(chunk => chunk.Content.Length),
            chunks.Count(chunk => chunk.Content.Length < _options.MinChunkChars),
            caseResults.Count == 0 ? 0d : caseResults.Average(result => result.HitAtTopK ? 1d : 0d),
            caseResults.Count == 0 ? 0d : caseResults.Average(result => result.ReciprocalRank),
            caseResults);
    }

    private int ResolveTopK(int? topK)
    {
        var resolved = topK.GetValueOrDefault(_options.DefaultTopK > 0 ? _options.DefaultTopK : 5);
        var maxTopK = _options.MaxTopK > 0 ? _options.MaxTopK : 10;
        return Math.Clamp(resolved, 1, maxTopK);
    }

    private static bool MatchesExpectedPhrase(string content, IReadOnlyList<string> expectedPhrases)
    {
        var normalizedContent = Normalize(content);
        return expectedPhrases.Any(phrase => normalizedContent.Contains(Normalize(phrase), StringComparison.Ordinal));
    }

    private static string Normalize(string text) =>
        CollapseWhitespace.Replace(text.Trim(), " ").ToLowerInvariant();

    private static string BuildPreview(string content)
    {
        var normalized = CollapseWhitespace.Replace(content.Trim(), " ");
        return normalized.Length <= 140 ? normalized : normalized[..140].TrimEnd() + "...";
    }

    private static double CosineDistance(float[] left, float[] right)
    {
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;

        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm <= 0 || rightNorm <= 0)
        {
            return 1d;
        }

        var similarity = dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
        return 1d - similarity;
    }

    private static ChunkingBenchmarkSummary Summarize(
        string strategy,
        IReadOnlyList<ChunkingStrategyBenchmarkResult> strategyResults)
    {
        return new ChunkingBenchmarkSummary(
            strategy,
            strategyResults.Sum(result => result.ChunkCount),
            strategyResults.Count == 0 ? 0d : strategyResults.Average(result => result.AverageChunkChars),
            strategyResults.Sum(result => result.TinyChunkCount),
            strategyResults.Count == 0 ? 0d : strategyResults.Average(result => result.RecallAtK),
            strategyResults.Count == 0 ? 0d : strategyResults.Average(result => result.MeanReciprocalRank));
    }
}
