using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// EF Core + Supabase Storage implementation of <see cref="IDocumentService"/>.
/// Sprint 1 scope: CRUD + filter. Sprint 2 invokes ingestion after PDF upload
/// so text chunks and embeddings are ready for RAG search.
/// </summary>
public sealed class DocumentService : IDocumentService
{
    /// <summary>50 MB cap (plan L5 + bucket config).</summary>
    public const long MaxFileSizeBytes = 50L * 1024 * 1024;

    /// <summary>Maximum documents allowed in a single folder.</summary>
    public const int MaxDocumentsPerFolder = 30;

    /// <summary>Signed download URL TTL — 5 minutes per plan L8.</summary>
    public const int SignedUrlTtlSeconds = 300;

    /// <summary>Bucket created in D2 (private + 50 MB + MIME whitelist).</summary>
    public const string BucketName = "documents";

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
        "application/vnd.openxmlformats-officedocument.presentationml.presentation", // .pptx
        "application/msword", // .doc (legacy, accepted but not chunked in D6)
        "application/vnd.ms-powerpoint", // .ppt (legacy)
    };

    private static readonly IReadOnlyDictionary<string, string> MimeTypesByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".doc"] = "application/msword",
            [".ppt"] = "application/vnd.ms-powerpoint",
        };

    private readonly AppDbContext _db;
    private readonly ISupabaseStorageClient _storage;
    private readonly IDocumentIngestionService? _ingestion;
    private readonly IStorageQuotaService _quota;
    private readonly ILogger<DocumentService> _logger;
    private readonly IStorageDeletionCoordinator _deletionCoordinator;
    private readonly IPlanCapacityGuard _capacityGuard;

    public DocumentService(
        AppDbContext db,
        ISupabaseStorageClient storage,
        IStorageQuotaService quota,
        ILogger<DocumentService> logger,
        IDocumentIngestionService? ingestion,
        IStorageDeletionCoordinator deletionCoordinator,
        IPlanCapacityGuard capacityGuard)
    {
        _db = db;
        _storage = storage;
        _quota = quota;
        _ingestion = ingestion;
        _logger = logger;
        _deletionCoordinator = deletionCoordinator;
        _capacityGuard = capacityGuard;
    }

    public async Task<DocumentDto> UploadAsync(
        Guid supabaseUserId,
        UploadDocumentRequest request,
        string fileName,
        string contentType,
        long fileSizeBytes,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // 1. Resolve the caller's domain user (public.users) from the GoTrue id.
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        if (!profile.IsActive)
        {
            throw new DocumentException(403, "user_inactive",
                "User account is inactive and cannot upload documents.");
        }

        // 2. Validate file size + MIME against the bucket policy.
        if (fileSizeBytes <= 0)
        {
            throw new DocumentException(400, "empty_file",
                "Uploaded file is empty.");
        }
        if (fileSizeBytes > MaxFileSizeBytes)
        {
            throw new DocumentException(413, "file_too_large",
                $"File exceeds the {MaxFileSizeBytes / (1024 * 1024)} MB upload limit.");
        }

        var canonicalContentType = ResolveCanonicalContentType(fileName, contentType);
        if (!AllowedMimeTypes.Contains(canonicalContentType))
        {
            throw new DocumentException(415, "unsupported_media_type",
                $"Content type '{contentType}' is not allowed. Accepted: PDF, DOC, DOCX, PPT, PPTX.");
        }

        // 3. Fast-fail known plan/folder violations before reserving bytes or uploading.
        // These checks are advisory: the serializable finalization below repeats them.
        await _quota.ValidateDocumentCountAsync(supabaseUserId, cancellationToken);
        if (request.FolderId.HasValue)
        {
            var folderOwned = await _db.Folders
                .AsNoTracking()
                .AnyAsync(f => f.Id == request.FolderId.Value && f.UserId == profile.Id, cancellationToken);
            if (!folderOwned)
            {
                throw new DocumentException(404, "folder_not_found",
                    "Folder does not exist or does not belong to the caller.");
            }

            var normalizedFileName = fileName.Trim();
            var currentFolderDocumentCount = await _db.Documents
                .CountAsync(document => document.FolderId == request.FolderId.Value, cancellationToken);
            if (currentFolderDocumentCount >= MaxDocumentsPerFolder)
            {
                throw new DocumentException(409, "folder_full",
                    $"This folder already has {currentFolderDocumentCount} document(s), which is the maximum ({MaxDocumentsPerFolder}).");
            }

            var duplicateFileName = await _db.Documents.AnyAsync(document =>
                document.FolderId == request.FolderId.Value
                && document.FileName.ToLower() == normalizedFileName.ToLower(), cancellationToken);
            if (duplicateFileName)
            {
                throw new DocumentException(409, "duplicate_file",
                    $"A file named \"{normalizedFileName}\" already exists in this folder.");
            }
        }

        // 5. Reserve quota before uploading to storage.
        StorageReservation? reservation = null;
        var storageUploadAttempted = false;
        var metadataCommitted = false;
        try
        {
            reservation = await _quota.ReserveUploadAsync(supabaseUserId, fileSizeBytes, cancellationToken);
        }
        catch (PlanException)
        {
            throw;
        }

        // 6. Compose a deterministic storage path: users/{user_id}/{yyyy}/{guid}-{slug}.
        // Including the user_id segment makes per-user enumeration easier in Storage UI
        // and lets us add bucket-level RLS later (e.g. only allow service-role + matching uid).
        var documentId = Guid.NewGuid();
        var slug = SanitizeFileName(fileName);
        var storagePath = $"users/{profile.Id:N}/{DateTimeOffset.UtcNow:yyyy}/{documentId:N}-{slug}";

        try
        {
            // 7. Upload bytes to Supabase Storage. If anything below fails, we attempt
            // a best-effort cleanup so we don't leak orphan objects.
            try
            {
                storageUploadAttempted = true;
                await _storage.UploadAsync(BucketName, storagePath, content, canonicalContentType,
                    upsert: false, cancellationToken: cancellationToken);
            }
            catch (SupabaseStorageException ex)
            {
                _logger.LogWarning(ex,
                    "Supabase Storage upload failed for {Path}. Is the local storage service running?",
                    storagePath);
                throw new DocumentException(503, "storage_unavailable",
                    "Document storage is unavailable. Start Supabase Storage and retry the upload.");
            }

            // 8. Atomically validate capacity and insert metadata after storage upload.
            var now = DateTimeOffset.UtcNow;
            var doc = new Document
            {
                Id = documentId,
                UserId = profile.Id,
                FolderId = request.FolderId,
                FileName = fileName.Trim(),
                StoragePath = storagePath,
                FileSizeBytes = fileSizeBytes,
                MimeType = canonicalContentType,
                SubjectCode = request.SubjectCode.Trim().ToUpperInvariant(),
                Semester = request.Semester.Trim().ToUpperInvariant(),
                PageCount = null,
                Status = IsIngestionCandidate(canonicalContentType) ? DocumentStatus.Processing : DocumentStatus.Ready,
                ErrorMessage = null,
                CreatedAt = now,
                UpdatedAt = now,
            };

            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                try
                {
                    await _capacityGuard.LockAndValidateAsync(_db, profile.Id,
                        new PlanCapacityRequest(1, 0, request.FolderId, request.FolderId.HasValue ? 1 : 0, 0), cancellationToken);
                    if (request.FolderId.HasValue)
                    {
                        var folder = await _db.Folders.SingleOrDefaultAsync(f => f.Id == request.FolderId.Value && f.UserId == profile.Id, cancellationToken)
                            ?? throw new DocumentException(404, "folder_not_found", "Folder does not exist or does not belong to the caller.");
                        var folderDocumentCount = await _db.Documents
                            .CountAsync(document => document.FolderId == folder.Id, cancellationToken);
                        if (folderDocumentCount >= MaxDocumentsPerFolder)
                        {
                            throw new DocumentException(409, "folder_full",
                                $"This folder already has {folderDocumentCount} document(s), which is the maximum ({MaxDocumentsPerFolder}).");
                        }
                        if (await _db.Documents.AnyAsync(d => d.FolderId == folder.Id && d.FileName.ToLower() == doc.FileName.ToLower(), cancellationToken))
                            throw new DocumentException(409, "duplicate_file", $"A file named \"{doc.FileName}\" already exists in this folder.");
                        folder.UpdatedAt = now;
                    }
                    _db.Documents.Add(doc);
                    await _db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                    metadataCommitted = true;
                }
                catch
                {
                    try { await tx.RollbackAsync(CancellationToken.None); } catch (Exception rollbackException) { _logger.LogError(rollbackException, "Upload metadata rollback failed."); }
                    throw;
                }
            }
            catch (Exception ex)
            {
                if (ex is PlanException or DocumentException) throw;
                throw new DocumentException(500, "upload_persist_failed", "Failed to persist document metadata after storage upload.");
            }

            // Storage upload + metadata insert both succeeded — confirm the quota reservation.
            await _quota.ConfirmReservationAsync(reservation, cancellationToken);

            _logger.LogInformation(
                "Document uploaded: id={Id} user={UserId} subject={Subject} semester={Semester} size={Size}B path={Path}",
                doc.Id, profile.Id, doc.SubjectCode, doc.Semester, doc.FileSizeBytes, doc.StoragePath);

            if (_ingestion is not null && IsIngestionCandidate(canonicalContentType))
            {
                var ingestion = await _ingestion.IngestAsync(doc.Id, supabaseUserId, cancellationToken);
                if (!ingestion.Success)
                {
                    _logger.LogWarning(
                        "Document ingestion finished with failure after upload: id={Id} error={Error}",
                        doc.Id, ingestion.ErrorMessage);
                }

                var reloaded = await _db.Documents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == doc.Id, cancellationToken);
                if (reloaded is not null)
                {
                    return ToDto(reloaded, signedUrl: null);
                }
            }

            return ToDto(doc, signedUrl: null);
        }
        catch
        {
            if (!metadataCommitted)
            {
                await CompensateFailedUploadAsync(reservation, storageUploadAttempted, storagePath);
            }
            throw;
        }
    }

    private async Task CompensateFailedUploadAsync(
        StorageReservation? reservation,
        bool storageUploadAttempted,
        string storagePath)
    {
        if (reservation is null)
        {
            return;
        }

        if (storageUploadAttempted)
        {
            try
            {
                // Delete is intentionally attempted even when UploadAsync threw: a timeout
                // may occur after Supabase has accepted the object. Storage treats 404 as success.
                await _storage.DeleteAsync(BucketName, storagePath, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(cleanupException,
                    "Storage cleanup failed after an attempted upload; reservation retained for manual reconciliation.");
                return;
            }
        }

        try
        {
            await _quota.ReleaseReservationAsync(reservation, CancellationToken.None);
        }
        catch (Exception releaseException)
        {
            _logger.LogError(releaseException,
                "Storage reservation release failed after upload failure; reservation retained for manual reconciliation.");
        }
    }

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(
        Guid supabaseUserId,
        DocumentListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        var q = _db.Documents.AsNoTracking();
        if (query.FolderId.HasValue)
        {
            q = q.Where(d => d.FolderId == query.FolderId.Value &&
                            (d.UserId == profile.Id || (d.Folder != null && d.Folder.ShareStatus == FolderStatus.Approved)));
        }
        else
        {
            q = q.Where(d => d.UserId == profile.Id);
        }

        if (!string.IsNullOrWhiteSpace(query.SubjectCode))
        {
            var subject = query.SubjectCode.Trim().ToUpperInvariant();
            q = q.Where(d => d.SubjectCode == subject);
        }
        if (!string.IsNullOrWhiteSpace(query.Semester))
        {
            var semester = query.Semester.Trim().ToUpperInvariant();
            q = q.Where(d => d.Semester == semester);
        }
        if (query.FolderId.HasValue)
        {
            q = q.Where(d => d.FolderId == query.FolderId.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var like = $"%{query.Q.Trim()}%";
            q = q.Where(d => EF.Functions.ILike(d.FileName, like));
        }

        var rows = await q.OrderByDescending(d => d.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(d => ToDto(d, signedUrl: null)).ToList();
    }

    public async Task<DocumentDto> GetByIdAsync(
        Guid supabaseUserId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        var doc = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && (d.UserId == profile.Id || (d.Folder != null && d.Folder.ShareStatus == FolderStatus.Approved)), cancellationToken)
            ?? throw new DocumentException(404, "document_not_found",
                "Document does not exist or does not belong to the caller.");

        var signedUrl = await _storage.CreateSignedUrlAsync(
            BucketName, doc.StoragePath, SignedUrlTtlSeconds, cancellationToken);

        return ToDto(doc, signedUrl);
    }

    public async Task<DocumentDto> MoveToFolderAsync(
        Guid supabaseUserId,
        Guid documentId,
        Guid? folderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "document_not_found",
                "Document does not exist or does not belong to the caller.");

        if (folderId.HasValue)
        {
            var folderOwned = await _db.Folders
                .AsNoTracking()
                .AnyAsync(f => f.Id == folderId.Value && f.UserId == profile.Id, cancellationToken);
            if (!folderOwned)
            {
                throw new DocumentException(404, "folder_not_found",
                    "Folder does not exist or does not belong to the caller.");
            }
        }

        doc.FolderId = folderId;
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(doc, signedUrl: null);
    }

    public async Task<DocumentDto> RenameAsync(
        Guid supabaseUserId,
        Guid documentId,
        string newFileName,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "document_not_found",
                "Document does not exist or does not belong to the caller.");

        var trimmed = newFileName.Trim();
        if (trimmed.Length == 0)
        {
            throw new DocumentException(400, "invalid_name", "File name cannot be empty.");
        }
        if (trimmed.Length > 255)
        {
            throw new DocumentException(400, "invalid_name", "File name cannot exceed 255 characters.");
        }

        doc.FileName = trimmed;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(doc, signedUrl: null);
    }

    public async Task DeleteAsync(
        Guid supabaseUserId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        if (!await _deletionCoordinator.DeleteOwnedDocumentAsync(documentId, profile.Id, cancellationToken))
        {
            throw new DocumentException(404, "document_not_found", "Document does not exist or does not belong to the caller.");
        }
    }

    public async Task<DocumentContentDto> GetContentAsync(
        Guid supabaseUserId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        var doc = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && (d.UserId == profile.Id || (d.Folder != null && d.Folder.ShareStatus == FolderStatus.Approved)), cancellationToken)
            ?? throw new DocumentException(404, "document_not_found",
                "Document does not exist or does not belong to the caller.");

        var chunks = await _db.DocumentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => new DocumentContentChunkDto(
                c.ChunkIndex,
                c.PageNumber,
                c.Content,
                c.TokenCount))
            .ToListAsync(cancellationToken);

        return new DocumentContentDto(
            doc.Id,
            doc.FileName,
            doc.MimeType,
            chunks.Count,
            doc.PageCount,
            chunks);
    }

    public async Task<string> GetFileViewUrlAsync(
        Guid supabaseUserId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        var doc = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && (d.UserId == profile.Id || (d.Folder != null && d.Folder.ShareStatus == FolderStatus.Approved)), cancellationToken)
            ?? throw new DocumentException(404, "document_not_found",
                "Document does not exist or does not belong to the caller.");

        if (!string.Equals(doc.MimeType,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(doc.MimeType,
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(doc.MimeType,
                "application/msword",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(doc.MimeType,
                "application/vnd.ms-powerpoint",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentException(400, "unsupported_format",
                "Microsoft Office Viewer only supports the document formats DOCX, DOC, PPTX, PPT.");
        }

        // Generate a signed URL (same mechanism as GetByIdAsync — proven to work).
        var signedUrl = await _storage.CreateSignedUrlAsync(
            BucketName, doc.StoragePath, 300, cancellationToken);

        return signedUrl;
    }

    private static string ResolveCanonicalContentType(string fileName, string? contentType)
    {
        var normalized = contentType?.Trim() ?? string.Empty;
        if (AllowedMimeTypes.Contains(normalized))
        {
            return normalized;
        }

        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(fileName);
            if (MimeTypesByExtension.TryGetValue(extension, out var canonicalContentType))
            {
                return canonicalContentType;
            }
        }

        return normalized;
    }

    private static bool IsIngestionCandidate(string contentType) => contentType switch
    {
        "application/pdf" => true,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => true,
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => true,
        _ => false,
    };

    private static DocumentDto ToDto(Document doc, string? signedUrl) => new()
    {
        Id = doc.Id,
        FolderId = doc.FolderId,
        FileName = doc.FileName,
        FileSizeBytes = doc.FileSizeBytes,
        MimeType = doc.MimeType,
        SubjectCode = doc.SubjectCode,
        Semester = doc.Semester,
        PageCount = doc.PageCount,
        Status = doc.Status,
        ErrorMessage = doc.ErrorMessage,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
        DownloadUrl = signedUrl,
        ReviewStatus = doc.ReviewStatus,
    };

    /// <summary>
    /// Strip path separators / dangerous chars from an upload filename so it's safe
    /// to embed in a storage path. We keep the extension if present.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var trimmed = Path.GetFileName(fileName).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "upload.bin";
        }
        var safe = new string(trimmed.Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
        // Cap length to keep object keys reasonable.
        return safe.Length > 80 ? safe[..80] : safe;
    }
}
