using System.Diagnostics;
using System.Text.RegularExpressions;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag;

public sealed class RagSearchService : IRagSearchService
{
    private const int ExcerptMaxChars = 500;

    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}_]+", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embeddingService;
    private readonly IReRankService _reRankService;
    private readonly RagOptions _options;
    private readonly string _currentModel;
    private readonly ILogger<RagSearchService> _logger;

    public RagSearchService(
        AppDbContext db,
        IEmbeddingService embeddingService,
        IReRankService reRankService,
        IOptions<RagOptions> options,
        IOptions<OllamaOptions> ollamaOptions,
        ILogger<RagSearchService> logger)
    {
        _db = db;
        _embeddingService = embeddingService;
        _reRankService = reRankService;
        _options = options.Value;
        _currentModel = ollamaOptions.Value.Model;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(
        Guid supabaseUserId,
        RagSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (supabaseUserId == Guid.Empty)
        {
            throw new DocumentException(401, "missing_user_id", "Authenticated Supabase user id is missing or invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new DocumentException(400, "empty_query", "Search query is required.");
        }

        EnsureOptionDimensions();
        var stopwatch = Stopwatch.StartNew();

        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found", "Authenticated user has no profile in public.users.");

        if (!profile.IsActive)
        {
            throw new DocumentException(403, "user_inactive", "User account is inactive and cannot search documents.");
        }

        var searchMode = ResolveSearchMode(request.SearchMode);
        var topK = ResolveTopK(request.TopK);
        var needsEmbedding = searchMode is RagSearchMode.Vector or RagSearchMode.Hybrid;
        float[]? queryEmbedding = null;

        if (needsEmbedding)
        {
            queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.Query, cancellationToken);
            ValidateEmbedding(queryEmbedding);
        }

        var chunks = await ApplyFilters(_db.DocumentChunks.AsNoTracking().Include(c => c.Document), profile.Id, request)
            .ToListAsync(cancellationToken);

        var rows = chunks
            .Select(c => new SearchRow(
                c.DocumentId,
                c.Document.FileName,
                c.ChunkIndex,
                c.PageNumber,
                c.Content,
                c.Embedding.ToArray(),
                DenseScore: 0d,
                KeywordScore: 0d,
                InitialScore: 0d,
                ReRankScore: null))
            .ToList();

        var scoredRows = ScoreRows(rows, request.Query, queryEmbedding, searchMode);
        var candidateCount = ResolveCandidateCount(topK);

        var rankedRows = scoredRows
            .OrderByDescending(row => row.InitialScore)
            .ThenByDescending(row => row.KeywordScore)
            .ThenByDescending(row => row.DenseScore)
            .ThenBy(row => row.ChunkIndex)
            .Take(candidateCount)
            .ToList();

        if (_options.ReRankEnabled)
        {
            var rerankTopN = ResolveReRankTopN(topK);
            var rerankCandidates = rankedRows
                .Select(row => new ReRankCandidate(
                    row.DocumentId,
                    row.FileName,
                    row.ChunkIndex,
                    row.PageNumber,
                    row.Content,
                    row.DenseScore,
                    row.KeywordScore,
                    row.InitialScore))
                .ToList();

            var reranked = await _reRankService.ReRankAsync(request.Query, rerankCandidates, rerankTopN, cancellationToken);

            rankedRows = reranked
                .Select(candidate => rankedRows.First(row =>
                    row.DocumentId == candidate.DocumentId &&
                    row.ChunkIndex == candidate.ChunkIndex &&
                    row.PageNumber == candidate.PageNumber) with
                {
                    ReRankScore = candidate.ReRankScore
                })
                .OrderByDescending(row => row.ReRankScore ?? row.InitialScore)
                .ThenByDescending(row => row.InitialScore)
                .ThenBy(row => row.ChunkIndex)
                .ToList();
        }

        var results = rankedRows
            .Take(topK)
            .Select(ToDto)
            .ToList();

        stopwatch.Stop();
        _logger.LogInformation(
            "RAG search completed: mode={Mode}, topK={TopK}, candidates={Candidates}, results={Results}, latency_ms={LatencyMs}",
            searchMode.ToString().ToLowerInvariant(),
            topK,
            scoredRows.Count,
            results.Count,
            stopwatch.ElapsedMilliseconds);

        if (stopwatch.ElapsedMilliseconds > 2000)
        {
            _logger.LogWarning(
                "RAG search latency exceeded threshold: mode={Mode}, latency_ms={LatencyMs}, query_length={QueryLength}",
                searchMode.ToString().ToLowerInvariant(),
                stopwatch.ElapsedMilliseconds,
                request.Query.Length);
        }

        return results;
    }

    private List<SearchRow> ScoreRows(
        IReadOnlyList<SearchRow> rows,
        string query,
        float[]? queryEmbedding,
        RagSearchMode searchMode)
    {
        var normalizedQuery = Normalize(query);
        var queryTokens = Tokenize(query);

        return rows
            .Select(row =>
            {
                var keywordScore = ComputeKeywordScore(normalizedQuery, queryTokens, row.Content);
                var denseScore = queryEmbedding is null
                    ? 0d
                    : 1d - CosineDistance(row.Embedding, queryEmbedding);

                var initialScore = searchMode switch
                {
                    RagSearchMode.Keyword => keywordScore,
                    RagSearchMode.Hybrid when _options.HybridSearchEnabled =>
                        (_options.VectorWeight * denseScore) + ((1d - _options.VectorWeight) * keywordScore),
                    _ => denseScore
                };

                return row with
                {
                    DenseScore = denseScore,
                    KeywordScore = keywordScore,
                    InitialScore = initialScore
                };
            })
            .ToList();
    }

    private IQueryable<DocumentChunk> ApplyFilters(
        IQueryable<DocumentChunk> query,
        Guid userId,
        RagSearchRequest request)
    {
        query = query.Where(c =>
            c.Document.UserId == userId &&
            c.Document.Status == DocumentStatus.Ready &&
            c.EmbeddingModel == _currentModel);

        if (request.DocumentId.HasValue)
        {
            query = query.Where(c => c.DocumentId == request.DocumentId.Value);
        }
        else if (request.DocumentIds is { Count: > 0 })
        {
            var documentIds = request.DocumentIds.Distinct().ToArray();
            query = query.Where(c => documentIds.Contains(c.DocumentId));
        }

        if (request.FolderId.HasValue)
        {
            query = query.Where(c => c.Document.FolderId == request.FolderId.Value);
        }

        var subjectCode = NormalizeFilter(request.SubjectCode);
        if (subjectCode is not null)
        {
            query = query.Where(c => c.Document.SubjectCode == subjectCode);
        }

        var semester = NormalizeFilter(request.Semester);
        if (semester is not null)
        {
            query = query.Where(c => c.Document.Semester == semester);
        }

        var keyword = request.TopicKeyword?.Trim();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(c => EF.Functions.ILike(c.Content, "%" + keyword + "%"));
        }

        return query;
    }

    private RagSearchResultDto ToDto(SearchRow row)
    {
        return new RagSearchResultDto(
            SourceLabel: BuildSourceLabel(row.FileName, row.ChunkIndex, row.PageNumber),
            DocumentId: row.DocumentId,
            FileName: row.FileName,
            ChunkIndex: row.ChunkIndex,
            PageNumber: row.PageNumber,
            ContentExcerpt: BuildExcerpt(row.Content),
            Score: row.ReRankScore ?? row.InitialScore);
    }

    private RagSearchMode ResolveSearchMode(string? requestedMode)
    {
        var raw = string.IsNullOrWhiteSpace(requestedMode) ? _options.SearchMode : requestedMode;
        return raw?.Trim().ToLowerInvariant() switch
        {
            "keyword" => RagSearchMode.Keyword,
            "hybrid" when _options.HybridSearchEnabled => RagSearchMode.Hybrid,
            _ => RagSearchMode.Vector
        };
    }

    private int ResolveTopK(int requestedTopK)
    {
        var defaultTopK = _options.DefaultTopK > 0 ? _options.DefaultTopK : 5;
        var maxTopK = _options.MaxTopK > 0 ? _options.MaxTopK : 10;
        var topK = requestedTopK > 0 ? requestedTopK : defaultTopK;
        return Math.Clamp(topK, 1, maxTopK);
    }

    private int ResolveCandidateCount(int topK)
    {
        var configured = _options.ReRankCandidateCount > 0 ? _options.ReRankCandidateCount : 20;
        return Math.Max(topK, configured);
    }

    private int ResolveReRankTopN(int topK)
    {
        var configured = _options.ReRankTopN > 0 ? _options.ReRankTopN : topK;
        return Math.Max(topK, configured);
    }

    private void EnsureOptionDimensions()
    {
        if (_options.EmbeddingDimensions != DocumentChunk.EmbeddingDimension)
        {
            throw new InvalidOperationException(
                $"Rag embedding dimension must remain {DocumentChunk.EmbeddingDimension} to match document_chunks.embedding.");
        }
    }

    private static void ValidateEmbedding(float[] embedding)
    {
        if (embedding.Length != DocumentChunk.EmbeddingDimension)
        {
            throw new InvalidOperationException(
                $"Embedding service returned {embedding.Length} dimensions; expected {DocumentChunk.EmbeddingDimension}.");
        }
    }

    private static string? NormalizeFilter(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToUpperInvariant();
    }

    private static string BuildSourceLabel(string fileName, int chunkIndex, int? pageNumber)
    {
        var pagePart = pageNumber.HasValue ? $", p. {pageNumber.Value}" : string.Empty;
        return $"{fileName} (chunk {chunkIndex}{pagePart})";
    }

    private static string BuildExcerpt(string content)
    {
        var collapsed = string.Join(' ', (content ?? string.Empty).Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return collapsed.Length <= ExcerptMaxChars
            ? collapsed
            : collapsed[..ExcerptMaxChars].TrimEnd() + "...";
    }

    private static double ComputeKeywordScore(string normalizedQuery, HashSet<string> queryTokens, string content)
    {
        var normalizedContent = Normalize(content);
        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            return 0d;
        }

        var contentTokens = Tokenize(content);
        var overlap = queryTokens.Count == 0
            ? 0d
            : queryTokens.Count(token => contentTokens.Contains(token)) / (double)queryTokens.Count;
        var phraseBonus = normalizedContent.Contains(normalizedQuery, StringComparison.Ordinal) ? 1d : 0d;
        var startsWithBonus = normalizedContent.StartsWith(normalizedQuery, StringComparison.Ordinal) ? 0.2d : 0d;

        return Math.Min(1.2d, overlap + phraseBonus + startsWithBonus);
    }

    private static string Normalize(string text) =>
        string.Join(' ', TokenRegex.Matches(text ?? string.Empty).Select(match => match.Value.ToLowerInvariant()));

    private static HashSet<string> Tokenize(string text) =>
        TokenRegex.Matches(text ?? string.Empty)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(token => token.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

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

    private enum RagSearchMode
    {
        Vector,
        Hybrid,
        Keyword
    }

    private sealed record SearchRow(
        Guid DocumentId,
        string FileName,
        int ChunkIndex,
        int? PageNumber,
        string Content,
        float[] Embedding,
        double DenseScore,
        double KeywordScore,
        double InitialScore,
        double? ReRankScore);
}
