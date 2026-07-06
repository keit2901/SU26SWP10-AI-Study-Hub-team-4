using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class DocumentModerationService : IDocumentModerationService
{
    private const string PendingReviewStatus = "Pending Review";
    private const string ApprovedStatus = "Approved";
    private const string RejectedStatus = "Rejected";
    private const string PendingProcessing = "Pending";
    private const string ProcessingProcessing = "Processing";
    private const string IndexedProcessing = "Indexed";
    private const string FailedProcessing = "Failed";
    private const string HighSeverity = "High";
    private const string MediumSeverity = "Medium";
    private const string LowSeverity = "Low";

    private readonly AppDbContext _db;
    private readonly ISupabaseStorageClient _storage;

    public DocumentModerationService(AppDbContext db, ISupabaseStorageClient storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<IReadOnlyList<ModerationQueueDocumentDto>> GetQueueAsync(CancellationToken ct = default)
    {
        var docs = await _db.Documents
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Chunks)
            .Where(d => d.Status != DocumentStatus.Ready)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        var docIds = docs.Select(d => d.Id).ToList();
        var firstChunkByDocumentId = await _db.DocumentChunks
            .AsNoTracking()
            .Where(chunk => docIds.Contains(chunk.DocumentId))
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new
            {
                chunk.DocumentId,
                chunk.Content
            })
            .ToListAsync(ct);

        var previewLookup = firstChunkByDocumentId
            .GroupBy(item => item.DocumentId)
            .ToDictionary(group => group.Key, group => group.First().Content);

        return docs.Select(doc =>
        {
            var previewText = previewLookup.TryGetValue(doc.Id, out var chunkPreview) && !string.IsNullOrWhiteSpace(chunkPreview)
                ? chunkPreview
                : BuildPreviewFallback(doc);

            return new ModerationQueueDocumentDto(
                doc.Id,
                doc.FileName,
                doc.SubjectCode,
                doc.Semester,
                doc.User.FullName ?? doc.User.Username,
                doc.User.Username,
                GetFileType(doc.MimeType),
                doc.FileSizeBytes,
                doc.StoragePath,
                MapProcessingStatus(doc.Status),
                MapModerationStatus(doc.Status),
                MapSeverity(doc.Status, doc.ErrorMessage),
                BuildReportReason(doc),
                doc.Status == DocumentStatus.Failed ? (doc.ErrorMessage ?? "Rejected during moderation.") : string.Empty,
                previewText,
                doc.Chunks.Count,
                doc.CreatedAt,
                doc.UpdatedAt);
        }).ToList();
    }

    public async Task<bool> ApproveAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
        {
            return false;
        }

        doc.Status = DocumentStatus.Ready;
        doc.ErrorMessage = null;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RejectAsync(Guid documentId, string? reason, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
        {
            return false;
        }

        doc.Status = DocumentStatus.Failed;
        doc.ErrorMessage = string.IsNullOrWhiteSpace(reason)
            ? "Rejected by moderator."
            : reason.Trim();
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null)
        {
            return false;
        }

        await _storage.DeleteAsync(DocumentService.BucketName, doc.StoragePath, ct);

        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> EscalateAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return false;

        doc.ReviewStatus = DocumentReviewStatus.None;
        doc.ErrorMessage = "Escalated to admin for final review.";
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RestoreAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return false;

        doc.Status = DocumentStatus.Processing;
        doc.ReviewStatus = DocumentReviewStatus.None;
        doc.ErrorMessage = null;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ModerationQueueDocumentDto>> GetEscalatedQueueAsync(CancellationToken ct = default)
    {
        var docs = await _db.Documents
            .AsNoTracking()
            .Include(d => d.User)
            .Include(d => d.Chunks)
            .Where(d => d.ErrorMessage != null && d.ErrorMessage.Contains("Escalated"))
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

        var docIds = docs.Select(d => d.Id).ToList();
        var firstChunkByDocumentId = await _db.DocumentChunks
            .AsNoTracking()
            .Where(chunk => docIds.Contains(chunk.DocumentId))
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new { chunk.DocumentId, chunk.Content })
            .ToListAsync(ct);

        var previewLookup = firstChunkByDocumentId
            .GroupBy(item => item.DocumentId)
            .ToDictionary(group => group.Key, group => group.First().Content);

        return docs.Select(doc =>
        {
            var previewText = previewLookup.TryGetValue(doc.Id, out var chunkPreview) && !string.IsNullOrWhiteSpace(chunkPreview)
                ? chunkPreview
                : BuildPreviewFallback(doc);

            return new ModerationQueueDocumentDto(
                doc.Id, doc.FileName, doc.SubjectCode, doc.Semester,
                doc.User.FullName ?? doc.User.Username, doc.User.Username,
                GetFileType(doc.MimeType), doc.FileSizeBytes, doc.StoragePath,
                MapProcessingStatus(doc.Status), "Escalated",
                MapSeverity(doc.Status, doc.ErrorMessage),
                doc.ErrorMessage ?? "Escalated for admin review.",
                doc.Status == DocumentStatus.Failed ? (doc.ErrorMessage ?? "Escalated during moderation.") : string.Empty,
                previewText, doc.Chunks.Count, doc.CreatedAt, doc.UpdatedAt);
        }).ToList();
    }

    private static string BuildPreviewFallback(Document doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.ErrorMessage))
        {
            return doc.ErrorMessage;
        }

        return doc.Status switch
        {
            DocumentStatus.Uploading => "Document upload is still being finalized before release.",
            DocumentStatus.Processing => "Document ingestion is in progress and needs moderation follow-up.",
            DocumentStatus.Failed => "Document processing failed and requires moderator review.",
            _ => "No extracted preview is available for this document yet."
        };
    }

    private static string BuildReportReason(Document doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.ErrorMessage))
        {
            return doc.ErrorMessage;
        }

        return doc.Status switch
        {
            DocumentStatus.Uploading => "Upload is pending final processing before release.",
            DocumentStatus.Processing => "Document is still processing and awaiting release review.",
            DocumentStatus.Failed => "Document processing failed and needs moderator attention.",
            _ => "Document review required."
        };
    }

    private static string MapProcessingStatus(DocumentStatus status) =>
        status switch
        {
            DocumentStatus.Uploading => PendingProcessing,
            DocumentStatus.Processing => ProcessingProcessing,
            DocumentStatus.Ready => IndexedProcessing,
            DocumentStatus.Failed => FailedProcessing,
            _ => PendingProcessing
        };

    private static string MapModerationStatus(DocumentStatus status) =>
        status switch
        {
            DocumentStatus.Ready => ApprovedStatus,
            DocumentStatus.Failed => RejectedStatus,
            _ => PendingReviewStatus
        };

    private static string MapSeverity(DocumentStatus status, string? errorMessage)
    {
        if (status == DocumentStatus.Failed)
        {
            return HighSeverity;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage) || status == DocumentStatus.Processing)
        {
            return MediumSeverity;
        }

        return LowSeverity;
    }

    private static string GetFileType(string mimeType) =>
        mimeType.ToLowerInvariant() switch
        {
            "application/pdf" => "pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            "application/msword" => "doc",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "pptx",
            "application/vnd.ms-powerpoint" => "ppt",
            _ => "file"
        };
}
