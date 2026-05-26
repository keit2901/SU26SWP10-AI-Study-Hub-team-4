using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services.Rag;

public interface ITextExtractionService
{
    Task<IReadOnlyList<ExtractedPage>> ExtractPagesAsync(
        Stream fileStream,
        string mimeType,
        CancellationToken cancellationToken = default);
}

public sealed record ExtractedPage(int? PageNumber, string Text);

public interface IChunkingService
{
    IReadOnlyList<DocumentChunkDraft> Chunk(Guid documentId, IReadOnlyList<ExtractedPage> pages);
}

public sealed record DocumentChunkDraft(Guid DocumentId, int ChunkIndex, int? PageNumber, string Content);

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public interface IRagSearchService
{
    Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(
        Guid supabaseUserId,
        RagSearchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IDocumentIngestionService
{
    Task<DocumentIngestionResult> IngestAsync(
        Guid documentId,
        Guid supabaseUserId,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentIngestionResult(
    Guid DocumentId,
    int ChunkCount,
    bool Success,
    string? ErrorMessage);
