using System.Diagnostics;
using AI_Study_Hub_v2.Options;

namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

internal static class ChunkingBenchmarkMetrics
{
    private static readonly string[] Unavailable =
    [
        "HeadingAssociationRetention",
        "SourceTextCoverage",
        "ListAtomSplitCount",
        "TableAtomSplitCount",
        "CrossPageOrSectionCount",
    ];

    public static (IReadOnlyList<DocumentChunkDraft> Chunks, double LatencyMs) Measure(IChunkingService chunker, Guid documentId, IReadOnlyList<ExtractedPage> pages)
    {
        var stopwatch = Stopwatch.StartNew();
        var chunks = chunker.Chunk(documentId, pages);
        stopwatch.Stop();
        return (chunks, stopwatch.Elapsed.TotalMilliseconds);
    }

    public static ChunkingIntrinsicMetrics Analyze(
        IReadOnlyList<DocumentChunkDraft> chunks,
        IReadOnlyList<ExtractedPage> pages,
        ITokenEstimator tokens,
        RagOptions options,
        double latencyMs)
    {
        var counts = chunks.Select(chunk => tokens.Estimate(chunk.Content)).Order().ToArray();
        var sourceTokens = pages.Sum(page => tokens.Estimate(page.Text));
        var inputTokens = counts.Sum();
        var below = counts.Count(count => count < options.SemanticMinTokens);
        var above = counts.Count(count => count > options.SemanticMaxTokens);

        return new ChunkingIntrinsicMetrics(
            EmbeddingCalls: chunks.Count,
            EstimatedInputTokenVolume: inputTokens,
            MinEstimatedTokens: counts.FirstOrDefault(),
            MeanEstimatedTokens: counts.Length == 0 ? 0d : counts.Average(),
            P50EstimatedTokens: Percentile(counts, 0.50),
            P90EstimatedTokens: Percentile(counts, 0.90),
            P95EstimatedTokens: Percentile(counts, 0.95),
            MaxEstimatedTokens: counts.LastOrDefault(),
            BelowMinimumCount: below,
            AboveMaximumCount: above,
            TinyOrphanRate: counts.Length == 0 ? 0d : below / (double)counts.Length,
            AdjacentOverlapEstimatedTokens: AverageAdjacentLineOverlap(chunks, tokens),
            MaxAdjacentLineOverlapEstimatedTokens: MaxAdjacentLineOverlap(chunks, tokens),
            EstimatedTokenExpansionRatio: sourceTokens == 0 ? 0d : Math.Max(0d, (inputTokens - sourceTokens) / (double)sourceTokens),
            HeadingAssociationRetention: null,
            SourceTextCoverage: null,
            ListAtomSplitCount: null,
            TableAtomSplitCount: null,
            CrossPageOrSectionCount: null,
            ChunkingLatencyMs: latencyMs,
            UnavailableMetrics: Unavailable,
            EstimatedTokenSamples: counts);
    }

    private static double Percentile(IReadOnlyList<int> sorted, double percentile)
    {
        if (sorted.Count == 0) { return 0d; }
        var index = Math.Clamp((int)Math.Ceiling(sorted.Count * percentile) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    // Public drafts do not retain atomic-unit identity. This is an exact line-boundary overlap lower bound,
    // rather than claiming a unit-aware overlap value that cannot be reconstructed from Phase 1 metadata.
    private static double AverageAdjacentLineOverlap(IReadOnlyList<DocumentChunkDraft> chunks, ITokenEstimator tokens)
    {
        if (chunks.Count < 2) { return 0d; }
        var overlap = 0;
        var eligible = 0;
        for (var index = 1; index < chunks.Count; index++)
        {
            var previous = chunks[index - 1];
            var current = chunks[index];
            if (previous.PageNumber != current.PageNumber || !string.Equals(previous.SectionTitle, current.SectionTitle, StringComparison.Ordinal)) { continue; }
            eligible++;
            overlap += tokens.Estimate(CommonLineOverlap(previous.Content, current.Content));
        }
        return eligible == 0 ? 0d : overlap / (double)eligible;
    }

    private static double MaxAdjacentLineOverlap(IReadOnlyList<DocumentChunkDraft> chunks, ITokenEstimator tokens)
    {
        var values = Enumerable.Range(1, Math.Max(0, chunks.Count - 1))
            .Where(index => chunks[index - 1].PageNumber == chunks[index].PageNumber && string.Equals(chunks[index - 1].SectionTitle, chunks[index].SectionTitle, StringComparison.Ordinal))
            .Select(index => (double)tokens.Estimate(CommonLineOverlap(chunks[index - 1].Content, chunks[index].Content)));
        return values.DefaultIfEmpty().Max();
    }

    private static string CommonLineOverlap(string previous, string current)
    {
        var tail = previous.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var head = current.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var maximum = Math.Min(tail.Length, head.Length);
        for (var count = maximum; count > 0; count--)
        {
            if (tail.Skip(tail.Length - count).SequenceEqual(head.Take(count), StringComparer.Ordinal))
            {
                return string.Join('\n', head.Take(count));
            }
        }
        return string.Empty;
    }
}
