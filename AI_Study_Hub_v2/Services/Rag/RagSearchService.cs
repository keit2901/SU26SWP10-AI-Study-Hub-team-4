using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services.Rag;

public sealed class RagSearchService : IRagSearchService
{
    private const int ExcerptMaxChars = 500;

    private readonly AppDbContext _db;
private readonly IEmbeddingService _embeddingService;
private readonly RagOptions _options;
private readonly string _currentModel;

    public RagSearchService(
    AppDbContext db,
    IEmbeddingService embeddingService,
    IOptions<RagOptions> options,
    IOptions<OllamaOptions> ollamaOptions)
{
    _db = db;
    _embeddingService = embeddingService;
    _options = options.Value;
    _currentModel = ollamaOptions.Value.Model;
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

        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found", "Authenticated user has no profile in public.users.");

        if (!profile.IsActive)
        {
            throw new DocumentException(403, "user_inactive", "User account is inactive and cannot search documents.");
        }

        var topK = ResolveTopK(request.TopK);
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.Query, cancellationToken);
        ValidateEmbedding(queryEmbedding);

        if (IsInMemoryProvider())
        {
            return await SearchInMemoryAsync(profile.Id, request, queryEmbedding, topK, cancellationToken);
        }

        return await SearchPostgresAsync(profile.Id, request, queryEmbedding, topK, cancellationToken);
    }

    private async Task<IReadOnlyList<RagSearchResultDto>> SearchPostgresAsync(
        Guid userId,
        RagSearchRequest request,
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        var queryVector = new Vector(queryEmbedding);
        var query = ApplyFilters(_db.DocumentChunks.AsNoTracking(), userId, request);

        var rows = await query
            .OrderBy(c => c.Embedding.CosineDistance(queryVector))
            .Select(c => new SearchRow(
                c.DocumentId,
                c.Document.FileName,
                c.ChunkIndex,
                c.PageNumber,
                c.Content,
                c.Embedding.CosineDistance(queryVector)))
            .Take(topK)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    private async Task<IReadOnlyList<RagSearchResultDto>> SearchInMemoryAsync(
        Guid userId,
        RagSearchRequest request,
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(_db.DocumentChunks.AsNoTracking().Include(c => c.Document), userId, request);
        var chunks = await query.ToListAsync(cancellationToken);

        return chunks
            .Select(c => new SearchRow(
                c.DocumentId,
                c.Document.FileName,
                c.ChunkIndex,
                c.PageNumber,
                c.Content,
                CosineDistance(c.Embedding.ToArray(), queryEmbedding)))
            .OrderBy(r => r.Distance)
            .Take(topK)
            .Select(ToDto)
            .ToList();
    }

    private IQueryable<DocumentChunk> ApplyFilters(
        IQueryable<DocumentChunk> query,
        Guid userId,
        RagSearchRequest request)
    {
query = query.Where(c =>
    c.Document.UserId == userId
    && c.Document.Status == DocumentStatus.Ready
    && c.EmbeddingModel == _currentModel);
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
            Score: 1d - row.Distance);
    }

    private int ResolveTopK(int requestedTopK)
    {
        var defaultTopK = _options.DefaultTopK > 0 ? _options.DefaultTopK : 5;
        var maxTopK = _options.MaxTopK > 0 ? _options.MaxTopK : 10;
        var topK = requestedTopK > 0 ? requestedTopK : defaultTopK;
        return Math.Clamp(topK, 1, maxTopK);
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

    private bool IsInMemoryProvider() =>
        string.Equals(_db.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal);

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

    private sealed record SearchRow(
        Guid DocumentId,
        string FileName,
        int ChunkIndex,
        int? PageNumber,
        string Content,
        double Distance);
}
