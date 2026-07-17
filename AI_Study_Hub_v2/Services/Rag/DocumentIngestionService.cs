using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;

namespace AI_Study_Hub_v2.Services.Rag;

public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private const int ErrorMessageMaxLength = 1000;

    private readonly AppDbContext _db;
    private readonly IDocumentStorageReadService _storageRead;
    private readonly ITextExtractionService _textExtraction;
    private readonly IChunkingService _chunking;
    private readonly IEmbeddingService _embedding;
    private readonly IImageDescriptionService _imageDescription;
    private readonly RagOptions _options;
    private readonly GroqOptions _groqOptions;
    private readonly ILogger<DocumentIngestionService> _logger;
    private readonly string _currentEmbeddingModel;

    public DocumentIngestionService(
        AppDbContext db,
        IDocumentStorageReadService storageRead,
        ITextExtractionService textExtraction,
        IChunkingService chunking,
        IEmbeddingService embedding,
        IImageDescriptionService imageDescription,
        IOptions<RagOptions> options,
        IOptions<OllamaOptions> ollamaOptions,
        IOptions<GroqOptions> groqOptions,
        ILogger<DocumentIngestionService> logger)
    {
        _db = db;
        _storageRead = storageRead;
        _textExtraction = textExtraction;
        _chunking = chunking;
        _embedding = embedding;
        _imageDescription = imageDescription;
        _options = options.Value;
        _groqOptions = groqOptions.Value;
        _logger = logger;
        _currentEmbeddingModel = ollamaOptions.Value.Model;
    }

    public async Task<DocumentIngestionResult> IngestAsync(
        Guid documentId,
        Guid supabaseUserId,
        CancellationToken cancellationToken = default)
    {
        Document? document = null;

        try
        {
            var profile = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken);
            if (profile is null)
            {
                return Failure(documentId, "Authenticated user has no profile in public.users.");
            }

            document = await _db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == profile.Id, cancellationToken);
            if (document is null)
            {
                return Failure(documentId, "Document does not exist or does not belong to the caller.");
            }

            document.Status = DocumentStatus.Processing;
            document.ErrorMessage = null;
            await _db.SaveChangesAsync(cancellationToken);

            using var fileStream = await _storageRead.OpenReadAsync(document, cancellationToken);
            var pages = await _textExtraction.ExtractPagesAsync(fileStream, document.MimeType, cancellationToken);

            var totalImages = pages.Sum(p => p.Images?.Count ?? 0);
            var maxImages = _groqOptions.MaxImagesPerDocument;
            var imagesSkipped = 0;

            if (totalImages > maxImages && _groqOptions.SkipImagesWhenLimitExceeded)
            {
                _logger.LogWarning(
                    "Document {DocumentId} has {TotalImages} images, exceeding limit of {MaxImages}. " +
                    "Truncating to first {MaxImages} images.",
                    document.Id, totalImages, maxImages, maxImages);
            }

            var remainingBudget = maxImages;

            foreach (var page in pages)
            {
                if (page.Images?.Count > 0)
                {
                    if (remainingBudget <= 0 && _groqOptions.SkipImagesWhenLimitExceeded)
                    {
                        imagesSkipped += page.Images.Count;
                        continue;
                    }

                    var pageImages = page.Images;
                    if (remainingBudget < pageImages.Count)
                    {
                        imagesSkipped += pageImages.Count - remainingBudget;
                        pageImages = pageImages.Take(remainingBudget).ToList();
                        remainingBudget = 0;
                    }
                    else
                    {
                        remainingBudget -= pageImages.Count;
                    }

                    var description = await _imageDescription.DescribeAsync(pageImages, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        page.Text = string.IsNullOrWhiteSpace(page.Text)
                            ? description
                            : page.Text + "\n\n" + description;
                    }
                }
            }

            var nonEmptyPageCount = pages.Count(p => !string.IsNullOrWhiteSpace(p.Text));
            if (nonEmptyPageCount == 0)
            {
                throw new InvalidOperationException("No extractable text found in the document.");
            }

            var drafts = _chunking.Chunk(document.Id, pages);
            if (drafts.Count == 0)
            {
                throw new InvalidOperationException("No chunks were produced from the extracted text.");
            }

          var now = DateTimeOffset.UtcNow;
var successfulChunkIndices = new HashSet<int>();
var successfulChunkCount = 0;
var failedChunkCount = 0;

foreach (var draft in drafts)
{
    float[] embedding;

    try
    {
        embedding = await _embedding.GenerateEmbeddingAsync(draft.Content, cancellationToken);

        if (embedding.Length != _options.EmbeddingDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding dimensions mismatch. Expected {_options.EmbeddingDimensions}, got {embedding.Length}.");
        }
    }
    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
    {
        failedChunkCount++;

        _logger.LogWarning(
            ex,
            "Skipping chunk {ChunkIndex} for document {DocumentId} because embedding generation failed.",
            draft.ChunkIndex,
            document.Id);

        continue;
    }

    var existingChunk = await _db.DocumentChunks
        .FirstOrDefaultAsync(
            c => c.DocumentId == document.Id && c.ChunkIndex == draft.ChunkIndex,
            cancellationToken);

    if (existingChunk is null)
    {
        _db.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
    DocumentId = document.Id,
    ChunkIndex = draft.ChunkIndex,
    PageNumber = draft.PageNumber,
    Content = draft.Content,
    TokenCount = EstimateTokenCount(draft.Content),
    Embedding = new Vector(embedding),
    EmbeddingModel = _currentEmbeddingModel,
    CreatedAt = now,

        });
    }
    else
    {
        existingChunk.PageNumber = draft.PageNumber;
existingChunk.Content = draft.Content;
existingChunk.TokenCount = EstimateTokenCount(draft.Content);
existingChunk.Embedding = new Vector(embedding);
existingChunk.EmbeddingModel = _currentEmbeddingModel;
    }

    await _db.SaveChangesAsync(cancellationToken);

    successfulChunkIndices.Add(draft.ChunkIndex);
    successfulChunkCount++;
}

if (successfulChunkCount == 0)
{
    document.PageCount = pages.Count;
    document.Status = DocumentStatus.Failed;
    document.ErrorMessage = "No chunks could be embedded.";

    await _db.SaveChangesAsync(cancellationToken);

    return Failure(document.Id, document.ErrorMessage);
}

var successfulChunkIndexList = successfulChunkIndices.ToList();

var staleChunks = await _db.DocumentChunks
    .Where(c => c.DocumentId == document.Id && !successfulChunkIndexList.Contains(c.ChunkIndex))
    .ToListAsync(cancellationToken);

_db.DocumentChunks.RemoveRange(staleChunks);

document.PageCount = pages.Count;
document.Status = DocumentStatus.Ready;
document.ErrorMessage = null;

await _db.SaveChangesAsync(cancellationToken);

_logger.LogInformation(
    "Document ingested: id={DocumentId} chunks={ChunkCount} pages={PageCount} failedChunks={FailedChunkCount}",
    document.Id,
    successfulChunkCount,
    document.PageCount,
    failedChunkCount);

return new DocumentIngestionResult(
    document.Id,
    successfulChunkCount,
    Success: true,
    ErrorMessage: null);
        }
        catch (Exception ex)
        {
            var isCancellation = ex is OperationCanceledException;
            _logger.LogWarning(ex, "Document ingestion failed: id={DocumentId}", documentId);
            var message = isCancellation ? "Ingestion was canceled or timed out." : TrimError(ex.Message);

            if (document is not null)
            {
                try
                {
                    _db.ChangeTracker.Clear();
                    var failedDocument = await _db.Documents
                        .FirstOrDefaultAsync(d => d.Id == document.Id, CancellationToken.None);
                    if (failedDocument is not null)
                    {
                        failedDocument.Status = DocumentStatus.Failed;
                        failedDocument.ErrorMessage = message;
                        await _db.SaveChangesAsync(CancellationToken.None);
                    }
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "Failed to persist ingestion failure state for document {DocumentId}.", document.Id);
                }
            }

            if (isCancellation)
            {
                throw;
            }

            return Failure(documentId, message);
        }
    }

    private static DocumentIngestionResult Failure(Guid documentId, string errorMessage) =>
        new(documentId, ChunkCount: 0, Success: false, ErrorMessage: errorMessage);

    private static int EstimateTokenCount(string content) => Math.Max(1, (int)Math.Ceiling(content.Length / 4d));

    private static string TrimError(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return "Document ingestion failed.";
        }

        return errorMessage.Length <= ErrorMessageMaxLength
            ? errorMessage
            : errorMessage[..ErrorMessageMaxLength];
    }
}
