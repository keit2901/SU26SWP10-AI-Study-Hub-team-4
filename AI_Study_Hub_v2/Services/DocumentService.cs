using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        AppDbContext db,
        ISupabaseStorageClient storage,
        ILogger<DocumentService> logger,
        IDocumentIngestionService? ingestion = null)
    {
        _db = db;
        _storage = storage;
        _ingestion = ingestion;
        _logger = logger;
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

        // 3. If folder specified, verify it belongs to the caller.
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
        }

        // 4. Compose a deterministic storage path: users/{user_id}/{yyyy}/{guid}-{slug}.
        // Including the user_id segment makes per-user enumeration easier in Storage UI
        // and lets us add bucket-level RLS later (e.g. only allow service-role + matching uid).
        var documentId = Guid.NewGuid();
        var slug = SanitizeFileName(fileName);
        var storagePath = $"users/{profile.Id:N}/{DateTimeOffset.UtcNow:yyyy}/{documentId:N}-{slug}";

        // 5. Upload bytes to Supabase Storage. If anything below fails, we attempt
        // a best-effort cleanup so we don't leak orphan objects.
        await _storage.UploadAsync(BucketName, storagePath, content, canonicalContentType,
            upsert: false, cancellationToken: cancellationToken);

        // 6. Insert metadata row. Status = Ready (file stored OK; chunking happens in D6).
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
            Status = DocumentStatus.Ready,
            ErrorMessage = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        try
        {
            _db.Documents.Add(doc);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Document row insert failed after storage upload succeeded. Cleaning up object {Path}.",
                storagePath);
            try
            {
                await _storage.DeleteAsync(BucketName, storagePath, CancellationToken.None);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx,
                    "Storage cleanup also failed for {Path}. Manual cleanup required.", storagePath);
            }
            throw new DocumentException(500, "upload_persist_failed",
                "Failed to persist document metadata after storage upload.");
        }

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

        var q = _db.Documents.AsNoTracking().Where(d => d.UserId == profile.Id);

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
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == profile.Id, cancellationToken)
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

        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "document_not_found",
                "Document does not exist or does not belong to the caller.");

        var pathToDelete = doc.StoragePath;

        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync(cancellationToken);

        // Storage delete is best-effort: a leaked object is recoverable, but a row
        // pointing at a missing object is worse for UX.
        try
        {
            await _storage.DeleteAsync(BucketName, pathToDelete, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Storage delete failed for {Path} after row removal. Object may be leaked.", pathToDelete);
        }
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

    private static bool IsIngestionCandidate(string contentType) =>
        string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

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
