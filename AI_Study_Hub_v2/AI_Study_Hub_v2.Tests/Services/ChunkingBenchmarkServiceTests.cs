using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using AI_Study_Hub_v2.Services.Rag.Benchmarking;
using System.Reflection;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class ChunkingBenchmarkServiceTests
{
    [Test]
    public async Task RunAsync_CraftedScenario_ShowsSemanticImprovementOverFixed()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RagOptions
        {
            ChunkingStrategy = "semantic",
            ChunkSizeChars = 24,
            ChunkOverlapChars = 0,
            MinChunkChars = 20,
            MaxSectionChars = 160,
            DefaultTopK = 5,
            MaxTopK = 5,
            EmbeddingDimensions = 8
        });

        var semantic = new ChunkingService(
            new BlockParser(),
            new SentenceSplitter(),
            new ChunkMerger(options));
        var fixedSize = new FixedSizeChunkingService(options);
        var estimator = new ConservativeTokenEstimator();
        var semanticV2 = new SemanticV2ChunkingService(estimator, options);
        var benchmark = new ChunkingBenchmarkService(
            semantic,
            fixedSize,
            semanticV2,
            estimator,
            new KeywordEmbeddingService(),
            options);

        var scenario = new ChunkingBenchmarkScenario(
            "crafted",
            "Crafted semantic win",
            new[]
            {
                new ExtractedPage(1, "Semantic retrieval preserves complete sentence meaning for benchmark scoring. Extra filler text.")
            },
            new[]
            {
                new ChunkingBenchmarkCase(
                    "C1",
                    "complete sentence meaning benchmark scoring",
                    new[] { "preserves complete sentence meaning for benchmark scoring" })
            });

        var result = await benchmark.RunAsync(5, new[] { scenario });

        result.Scenarios.Should().ContainSingle();
        result.Scenarios[0].Fixed.Strategy.Should().Be("fixed-v1");
        result.Scenarios[0].Semantic.Strategy.Should().Be("semantic-v1");
        result.Scenarios[0].SemanticV2!.Strategy.Should().Be("semantic-v2");
        result.Scenarios[0].Semantic.RecallAtK.Should().BeGreaterThan(result.Scenarios[0].Fixed.RecallAtK);
        result.Semantic.RecallAtK.Should().BeGreaterThan(result.Fixed.RecallAtK);
        result.SemanticV2!.Intrinsic!.EstimatedInputTokenVolume.Should().BeGreaterThan(0);
        result.SemanticV2.Intrinsic.MaxEstimatedTokens.Should().BeLessThanOrEqualTo(192);
        result.SemanticV2.RecallAt1.Should().BeGreaterThan(0);
        result.SemanticV2.RecallAt3.Should().BeGreaterThan(0);
        result.SemanticV2.RecallAt5.Should().BeGreaterThan(0);
        result.SemanticV2.NdcgAt5.Should().BeGreaterThan(0);
        result.SemanticV2.ZeroHitQueryCount.Should().Be(0);
        result.SemanticV2Gates!.ZeroAboveMax.Should().BeTrue();
        result.SemanticV2.Intrinsic.P95EstimatedTokens.Should().BeGreaterThanOrEqualTo(result.SemanticV2.Intrinsic.P50EstimatedTokens);
        result.SemanticV2.Intrinsic.AdjacentOverlapEstimatedTokens.Should().BeGreaterThanOrEqualTo(0);
        result.SemanticV2.Intrinsic.EstimatedTokenExpansionRatio.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task RunAsync_DefaultCorpus_UsesThreeStrategiesAndExpandedDeterministicCoverage()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RagOptions
        {
            ChunkSizeChars = 120,
            ChunkOverlapChars = 10,
            MinChunkChars = 20,
            MaxSectionChars = 700,
            EmbeddingDimensions = 8,
        });
        var estimator = new ConservativeTokenEstimator();
        var service = new ChunkingBenchmarkService(
            new ChunkingService(new BlockParser(), new SentenceSplitter(), new ChunkMerger(options)),
            new FixedSizeChunkingService(options),
            new SemanticV2ChunkingService(estimator, options),
            estimator,
            new KeywordEmbeddingService(),
            options);

        var result = await service.RunAsync();

        ChunkingBenchmarkDataset.All.Should().HaveCountGreaterOrEqualTo(10);
        ChunkingBenchmarkDataset.All.Sum(scenario => scenario.Cases.Count).Should().BeGreaterOrEqualTo(30);
        result.Scenarios.Should().HaveCount(ChunkingBenchmarkDataset.All.Count);
        result.SemanticV2.Should().NotBeNull();
        result.SemanticV2Gates.Should().NotBeNull();
        result.SemanticV2!.Intrinsic!.UnavailableMetrics.Should().Contain("HeadingAssociationRetention");
        result.SemanticV2Gates!.ScenarioRecallAt5Diagnostics.Should().HaveCount(result.Scenarios.Count);
        result.SemanticV2Gates.ListTableRecallAt3Diagnostics.Should().HaveCount(2);
        result.SemanticV2Gates.Passed.Should().BeFalse();
    }

    [Test]
    public void BenchmarkModels_KeepOriginalConstructionCompatibleWithAdditiveFields()
    {
        var strategy = new ChunkingStrategyBenchmarkResult("semantic-v1", 1, 20, 0, 1, 1, Array.Empty<ChunkingBenchmarkCaseResult>());
        var scenario = new ChunkingBenchmarkScenarioResult("id", "title", strategy, strategy);
        var summary = new ChunkingBenchmarkSummary("semantic-v1", 1, 20, 0, 1, 1);
        var comparison = new ChunkingBenchmarkComparisonResult(DateTimeOffset.UnixEpoch, 5, new[] { scenario }, summary, summary);

        scenario.SemanticV2.Should().BeNull();
        comparison.SemanticV2.Should().BeNull();
        comparison.SemanticV2Gates.Should().BeNull();
    }

    [Test]
    public async Task RunAsync_UsesConfiguredOverlapLimitAndCanonicalInRangeRate()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RagOptions
        {
            ChunkSizeChars = 120,
            ChunkOverlapChars = 0,
            MinChunkChars = 20,
            MaxSectionChars = 700,
            SemanticOverlapTokens = 12,
            EmbeddingDimensions = 8,
        });
        var estimator = new ConservativeTokenEstimator();
        var benchmark = new ChunkingBenchmarkService(
            new ChunkingService(new BlockParser(), new SentenceSplitter(), new ChunkMerger(options)),
            new FixedSizeChunkingService(options),
            new SemanticV2ChunkingService(estimator, options),
            estimator,
            new KeywordEmbeddingService(),
            options);

        var result = await benchmark.RunAsync(5, [new ChunkingBenchmarkScenario(
            "overlap", "Configured overlap gate", [new ExtractedPage(1, "Body")],
            [new ChunkingBenchmarkCase("case", "Body", ["Body"])])]);
        var gates = result.SemanticV2Gates!;

        gates.InRangeRate.Should().BeGreaterOrEqualTo(0d);
        gates.EligibleOverlapWithinLimit.Should().BeTrue();
    }

    [Test]
    public void EvaluateGates_ObservedOverlapTwenty_FailsWhenConfiguredLimitIsTwelve()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RagOptions { SemanticOverlapTokens = 12 });
        var estimator = new ConservativeTokenEstimator();
        var service = new ChunkingBenchmarkService(
            new ChunkingService(new BlockParser(), new SentenceSplitter(), new ChunkMerger(options)),
            new FixedSizeChunkingService(options),
            new SemanticV2ChunkingService(estimator, options),
            estimator,
            new KeywordEmbeddingService(),
            options);
        var baselineMetrics = Metrics(maxAdjacentOverlap: 0d);
        var v2Metrics = Metrics(maxAdjacentOverlap: 20d);
        var semantic = new ChunkingBenchmarkSummary("semantic-v1", 1, 100, 0, 1, 1, Intrinsic: baselineMetrics);
        var v2 = new ChunkingBenchmarkSummary("semantic-v2", 1, 100, 0, 1, 1, Intrinsic: v2Metrics);
        var evaluate = typeof(ChunkingBenchmarkService).GetMethod("EvaluateGates", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var gates = (SemanticV2ReleaseGates)evaluate.Invoke(service, [semantic, v2, Array.Empty<ChunkingBenchmarkScenarioResult>()])!;

        gates.EligibleOverlapWithinLimit.Should().BeFalse();
        gates.InRangeRate.Should().Be(1d);
#pragma warning disable CS0618 // Verify the documented serialization/API compatibility alias.
        gates.NonExceptionInRangeRate.Should().Be(gates.InRangeRate);
#pragma warning restore CS0618
    }

    private static ChunkingIntrinsicMetrics Metrics(double maxAdjacentOverlap) => new(
        EmbeddingCalls: 1,
        EstimatedInputTokenVolume: 100,
        MinEstimatedTokens: 120,
        MeanEstimatedTokens: 120d,
        P50EstimatedTokens: 120d,
        P90EstimatedTokens: 120d,
        P95EstimatedTokens: 120d,
        MaxEstimatedTokens: 120,
        BelowMinimumCount: 0,
        AboveMaximumCount: 0,
        TinyOrphanRate: 0d,
        AdjacentOverlapEstimatedTokens: maxAdjacentOverlap,
        MaxAdjacentLineOverlapEstimatedTokens: maxAdjacentOverlap,
        EstimatedTokenExpansionRatio: 1d,
        HeadingAssociationRetention: null,
        SourceTextCoverage: null,
        ListAtomSplitCount: null,
        TableAtomSplitCount: null,
        CrossPageOrSectionCount: null,
        ChunkingLatencyMs: 0d,
        UnavailableMetrics: Array.Empty<string>(),
        EstimatedTokenSamples: [120]);

    private sealed class KeywordEmbeddingService : IEmbeddingService
    {
        private static readonly string[] Vocabulary =
        {
            "semantic", "retrieval", "complete", "sentence",
            "meaning", "benchmark", "scoring", "fixed"
        };

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            var normalized = text.ToLowerInvariant();
            var vector = Vocabulary
                .Select(token => normalized.Contains(token, StringComparison.Ordinal) ? 1f : 0f)
                .ToArray();

            return Task.FromResult(vector);
        }
    }
}
