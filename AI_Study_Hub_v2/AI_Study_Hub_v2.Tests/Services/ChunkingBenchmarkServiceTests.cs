using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using AI_Study_Hub_v2.Services.Rag.Benchmarking;

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
        var benchmark = new ChunkingBenchmarkService(
            semantic,
            fixedSize,
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
        result.Scenarios[0].Semantic.RecallAtK.Should().BeGreaterThan(result.Scenarios[0].Fixed.RecallAtK);
        result.Semantic.RecallAtK.Should().BeGreaterThan(result.Fixed.RecallAtK);
    }

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
