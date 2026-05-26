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
    private readonly RagOptions _options;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        AppDbContext db,
        IDocumentStorageReadService storageRead,
        ITextExtractionService textExtraction,
        IChunkingService chunking,
        IEmbeddingService embedding,
        IOptions<RagOptions> options,
        ILogger<DocumentIngestionService> logger)
    {
        _db = db;
        _storageRead = storageRead;
        _textExtraction = textExtraction;
        _chunking = chunking;
        _embedding = embedding;
        _options = options.Value;
        _logger = logger;
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

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var oldChunks = _db.DocumentChunks.Where(c => c.DocumentId == document.Id);
            _db.DocumentChunks.RemoveRange(oldChunks);

            var now = DateTimeOffset.UtcNow;
            foreach (var draft in drafts)
            {
                var embedding = await _embedding.GenerateEmbeddingAsync(draft.Content, cancellationToken);
                if (embedding.Length != _options.EmbeddingDimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding dimensions mismatch. Expected {_options.EmbeddingDimensions}, got {embedding.Length}.");
                }

                _db.DocumentChunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    ChunkIndex = draft.ChunkIndex,
                    PageNumber = draft.PageNumber,
                    Content = draft.Content,
                    TokenCount = EstimateTokenCount(draft.Content),
                    Embedding = new Vector(embedding),
                    CreatedAt = now,
                });
            }

            document.PageCount = pages.Count;
            document.Status = DocumentStatus.Ready;
            document.ErrorMessage = null;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Document ingested: id={DocumentId} chunks={ChunkCount} pages={PageCount}",
                document.Id, drafts.Count, document.PageCount);

            return new DocumentIngestionResult(document.Id, drafts.Count, Success: true, ErrorMessage: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Document ingestion failed: id={DocumentId}", documentId);
            var message = TrimError(ex.Message);

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
