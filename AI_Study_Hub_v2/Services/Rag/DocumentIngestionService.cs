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
    private readonly ITokenEstimator _tokenEstimator;
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
        ITokenEstimator tokenEstimator,
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
        _tokenEstimator = tokenEstimator;
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
        Guid? operationId = null;

        try
        {
            var profile = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken);
            if (profile is null)
            {
                return Failure(documentId, "Authenticated user has no profile in public.users.");
            }

            var document = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == profile.Id, cancellationToken);
            if (document is null)
            {
                return Failure(documentId, "Document does not exist or does not belong to the caller.");
            }

            operationId = Guid.NewGuid();
            if (!await TryClaimAsync(document.Id, profile.Id, operationId.Value, cancellationToken))
            {
                return Failure(documentId, "Document does not exist or does not belong to the caller.");
            }

            using var fileStream = await _storageRead.OpenReadAsync(document, cancellationToken);
            var pages = await _textExtraction.ExtractPagesAsync(fileStream, document.MimeType, cancellationToken);

            var totalImages = pages.Sum(p => p.Images?.Count ?? 0);
            var maxImages = _groqOptions.MaxImagesPerDocument;
            var imagesSkipped = 0;

            if (totalImages > maxImages && _groqOptions.SkipImagesWhenLimitExceeded)
            {
                _logger.LogWarning(
                    "Document {DocumentId} has {TotalImages} images, exceeding limit of {MaxImages}. Truncating to first {MaxImages} images.",
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
            var preparedChunks = new List<(DocumentChunkDraft Draft, float[] Embedding)>(drafts.Count);

            // Do not modify chunks until every embedding succeeds. A failed re-ingestion
            // therefore keeps its prior complete chunk set rather than publishing a partial one.
            foreach (var draft in drafts)
            {
                var embedding = await _embedding.GenerateEmbeddingAsync(draft.Content, cancellationToken);
                if (embedding.Length != _options.EmbeddingDimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding dimensions mismatch. Expected {_options.EmbeddingDimensions}, got {embedding.Length}.");
                }

                preparedChunks.Add((draft, embedding));
            }

            if (!await TryPublishAsync(document.Id, operationId.Value, pages.Count, preparedChunks, now, cancellationToken))
            {
                return Superseded(document.Id);
            }

            _logger.LogInformation(
                "Document ingested: id={DocumentId} chunks={ChunkCount} pages={PageCount} imagesSkipped={ImagesSkipped}",
                document.Id, preparedChunks.Count, pages.Count, imagesSkipped);

            return new DocumentIngestionResult(document.Id, preparedChunks.Count, Success: true, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            var isCancellation = ex is OperationCanceledException;
            _logger.LogWarning(ex, "Document ingestion failed: id={DocumentId}", documentId);
            var message = isCancellation ? "Ingestion was canceled or timed out." : TrimError(ex.Message);

            if (isCancellation)
            {
                if (operationId is Guid ownedOperationId)
                {
                    try
                    {
                        await TryMarkFailedAsync(documentId, ownedOperationId, message, CancellationToken.None);
                    }
                    catch (Exception saveEx)
                    {
                        _logger.LogError(saveEx, "Failed to persist ingestion failure state for document {DocumentId}.", documentId);
                    }
                }

                throw;
            }

            if (operationId is Guid failureOperationId)
            {
                try
                {
                    if (!await TryMarkFailedAsync(documentId, failureOperationId, message, CancellationToken.None))
                        return Superseded(documentId);
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "Failed to persist ingestion failure state for document {DocumentId}.", documentId);
                }
            }

            return Failure(documentId, message);
        }
    }

    private static DocumentIngestionResult Failure(Guid documentId, string errorMessage) =>
        new(documentId, ChunkCount: 0, Success: false, ErrorMessage: errorMessage);

    private static DocumentIngestionResult Superseded(Guid documentId) =>
        Failure(documentId, "Ingestion was superseded by a newer operation.");

    private async Task<bool> TryClaimAsync(Guid documentId, Guid userId, Guid operationId, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            return await _db.Documents
                .Where(document => document.Id == documentId && document.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(document => document.IngestionOperationId, operationId)
                    .SetProperty(document => document.Status, DocumentStatus.Processing)
                    .SetProperty(document => document.ErrorMessage, (string?)null), cancellationToken) == 1;
        }

        var document = await _db.Documents
            .FirstOrDefaultAsync(item => item.Id == documentId && item.UserId == userId, cancellationToken);
        if (document is null)
            return false;

        document.IngestionOperationId = operationId;
        document.Status = DocumentStatus.Processing;
        document.ErrorMessage = null;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> TryPublishAsync(
        Guid documentId,
        Guid operationId,
        int pageCount,
        IReadOnlyList<(DocumentChunkDraft Draft, float[] Embedding)> preparedChunks,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var document = _db.Database.IsRelational()
                ? await _db.Documents.FromSqlInterpolated($"SELECT * FROM documents WHERE id = {documentId} FOR UPDATE")
                    .SingleOrDefaultAsync(cancellationToken)
                : await _db.Documents.SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
            if (document is null || document.IngestionOperationId != operationId)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(CancellationToken.None);
                return false;
            }

            var existingChunks = await _db.DocumentChunks
                .Where(chunk => chunk.DocumentId == documentId)
                .ToListAsync(cancellationToken);
            _db.DocumentChunks.RemoveRange(existingChunks);
            _db.DocumentChunks.AddRange(preparedChunks.Select(item => new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = item.Draft.ChunkIndex,
                PageNumber = item.Draft.PageNumber,
                Content = item.Draft.Content,
                TokenCount = _tokenEstimator.Estimate(item.Draft.Content),
                Embedding = new Vector(item.Embedding),
                EmbeddingModel = _currentEmbeddingModel,
                CreatedAt = createdAt,
            }));

            document.PageCount = pageCount;
            document.Status = DocumentStatus.Ready;
            document.ErrorMessage = null;
            document.IngestionOperationId = null;
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            if (transaction is not null)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            }
            throw;
        }
    }

    private async Task<bool> TryMarkFailedAsync(Guid documentId, Guid operationId, string errorMessage, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        if (_db.Database.IsRelational())
        {
            return await _db.Documents
                .Where(document => document.Id == documentId && document.IngestionOperationId == operationId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(document => document.Status, DocumentStatus.Failed)
                    .SetProperty(document => document.ErrorMessage, errorMessage)
                    .SetProperty(document => document.IngestionOperationId, (Guid?)null), cancellationToken) == 1;
        }

        var document = await _db.Documents.SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is null || document.IngestionOperationId != operationId)
            return false;

        document.Status = DocumentStatus.Failed;
        document.ErrorMessage = errorMessage;
        document.IngestionOperationId = null;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

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
