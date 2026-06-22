using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services.Rag;

public interface ITextExtractionService
{
    Task<IReadOnlyList<ExtractedPage>> ExtractPagesAsync(
        Stream fileStream,
        string mimeType,
        CancellationToken cancellationToken = default);
}

public sealed record ExtractedPage
{
    public int? PageNumber { get; init; }
    public string Text { get; set; }
    public IReadOnlyList<ExtractedImage>? Images { get; init; }

    public ExtractedPage(int? pageNumber, string text, IReadOnlyList<ExtractedImage>? images = null)
    {
        PageNumber = pageNumber;
        Text = text;
        Images = images;
    }

    public void Deconstruct(out int? pageNumber, out string text, out IReadOnlyList<ExtractedImage>? images)
    {
        pageNumber = PageNumber;
        text = Text;
        images = Images;
    }
}

public sealed record ExtractedImage(byte[] Data, string MimeType);

public interface IImageDescriptionService
{
    Task<string> DescribeAsync(
        IReadOnlyList<ExtractedImage> pageImages,
        CancellationToken cancellationToken = default);
}

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
