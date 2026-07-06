using System.Text.RegularExpressions;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag;

/// <summary>
/// Lightweight local re-ranker used as a Phase 3 fallback when no external cross-encoder is configured.
/// </summary>
public sealed class ReRankService : IReRankService
{
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}_]+", RegexOptions.Compiled);

    private readonly RagOptions _options;
    private readonly ILogger<ReRankService> _logger;

    public ReRankService(
        IOptions<RagOptions> options,
        ILogger<ReRankService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<ReRankCandidate>> ReRankAsync(
        string query,
        IReadOnlyList<ReRankCandidate> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (!_options.ReRankEnabled || candidates.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ReRankCandidate>>(candidates.Take(topN).ToList());
        }

        var normalizedQuery = Normalize(query);
        var queryTokens = Tokenize(normalizedQuery);

        var reranked = candidates
            .Select(candidate =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedContent = Normalize(candidate.Content);
                var contentTokens = Tokenize(normalizedContent);
                var tokenHits = queryTokens.Count == 0
                    ? 0d
                    : queryTokens.Count(token => contentTokens.Contains(token)) / (double)queryTokens.Count;
                var exactPhraseBonus = normalizedContent.Contains(normalizedQuery, StringComparison.Ordinal)
                    ? 1d
                    : 0d;
                var contentStartsWithQueryBonus = normalizedContent.StartsWith(normalizedQuery, StringComparison.Ordinal)
                    ? 0.25d
                    : 0d;

                var rerankScore =
                    candidate.DenseScore * 0.45d +
                    candidate.KeywordScore * 0.25d +
                    tokenHits * 0.20d +
                    exactPhraseBonus * 0.10d +
                    contentStartsWithQueryBonus;

                return candidate with { ReRankScore = rerankScore };
            })
            .OrderByDescending(candidate => candidate.ReRankScore ?? candidate.InitialScore)
            .ThenByDescending(candidate => candidate.KeywordScore)
            .ThenByDescending(candidate => candidate.DenseScore)
            .ThenBy(candidate => candidate.ChunkIndex)
            .Take(Math.Max(1, topN))
            .ToList();

        _logger.LogDebug("Re-rank completed: candidates={Candidates}, returned={Returned}", candidates.Count, reranked.Count);
        return Task.FromResult<IReadOnlyList<ReRankCandidate>>(reranked);
    }

    private static string Normalize(string text) =>
        string.Join(' ', TokenRegex.Matches(text ?? string.Empty).Select(match => match.Value.ToLowerInvariant()));

    private static HashSet<string> Tokenize(string text) =>
        TokenRegex.Matches(text)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(token => token.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
}
