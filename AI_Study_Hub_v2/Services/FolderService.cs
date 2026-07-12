using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class FolderService : IFolderService
{
    private readonly AppDbContext _db;
    private readonly ILogger<FolderService> _logger;
    private readonly ISupabaseStorageClient _storage;
    private readonly IFolderShareAiModerator _shareAiModerator;

    public FolderService(
        AppDbContext db,
        ILogger<FolderService> logger,
        ISupabaseStorageClient storage,
        IFolderShareAiModerator shareAiModerator)
    {
        _db = db;
        _logger = logger;
        _storage = storage;
        _shareAiModerator = shareAiModerator;
    }

    public async Task<IReadOnlyList<FolderDto>> ListAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);

        var rows = await _db.Folders
            .AsNoTracking()
            .Where(f => f.UserId == profile.Id)
            .OrderByDescending(f => f.IsFavorite)
            .ThenByDescending(f => f.UpdatedAt)
            .Select(f => new FolderDto
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                DocumentCount = f.Documents.Count,
                IsFavorite = f.IsFavorite,
                ShareStatus = f.ShareStatus,
                SharedAt = f.SharedAt,
                ShareReviewSource = f.ShareReviewSource,
                AiReviewReason = f.AiReviewReason,
                AiReviewConfidence = f.AiReviewConfidence,
                HumanReviewReason = f.HumanReviewReason,
                RequiresHumanReview = f.RequiresHumanReview,
                AppealRequestedAt = f.AppealRequestedAt,
                AppealMessage = f.AppealMessage,
                Icon = f.Icon,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
                Status = f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Rejected" :
                         f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                         f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready) ? (f.ShareStatus == FolderStatus.Approved ? "Shared" : "Pending Share") :
                         "Empty"
            })
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<FolderDto> CreateAsync(
        Guid supabaseUserId,
        CreateFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var name = NormalizeName(request.Name);
        var description = NormalizeDescription(request.Description);

        await EnsureUniqueNameAsync(profile.Id, name, excludeFolderId: null, cancellationToken);

        // Enforce plan-level folder count limit.
        await _quota.ValidateFolderCountAsync(supabaseUserId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = profile.Id,
            Name = name,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Folders.Add(folder);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Folder created: id={Id} user={UserId} name={Name}", folder.Id, profile.Id, folder.Name);
        return ToDto(folder, documentCount: 0);
    }

    public async Task<FolderDto> UpdateAsync(
        Guid supabaseUserId,
        Guid folderId,
        UpdateFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist or does not belong to the caller.");

        if (request.Name is not null)
        {
            var name = NormalizeName(request.Name);
            await EnsureUniqueNameAsync(profile.Id, name, excludeFolderId: folder.Id, cancellationToken);
            folder.Name = name;
        }
        if (request.Description is not null)
        {
            folder.Description = NormalizeDescription(request.Description);
        }
        if (request.Icon is not null)
        {
            folder.Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
        }
        if (request.IsFavorite.HasValue)
        {
            folder.IsFavorite = request.IsFavorite.Value;
        }
        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task DeleteAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist or does not belong to the caller.");

        var docIds = await _db.Documents
            .Where(d => d.FolderId == folder.Id)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        if (docIds.Count > 0)
        {
            var chunks = await _db.DocumentChunks
                .Where(c => docIds.Contains(c.DocumentId))
                .ExecuteDeleteAsync(cancellationToken);

            await _db.Documents
                .Where(d => d.FolderId == folder.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Folder deleted: id={Id} user={UserId} documents={DocCount}",
            folder.Id, profile.Id, docIds.Count);
    }

    public async Task<FolderDto> ToggleFavoriteAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist or does not belong to the caller.");

        folder.IsFavorite = !folder.IsFavorite;
        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task<IReadOnlyList<FolderDto>> ListSharedAsync(
        Guid? supabaseUserId = null,
        CancellationToken cancellationToken = default)
    {
        Guid? currentProfileId = null;
        if (supabaseUserId.HasValue)
        {
            currentProfileId = (await ResolveProfileAsync(
                supabaseUserId.Value,
                cancellationToken)).Id;
        }

        var rows = await _db.Folders
            .AsNoTracking()
            .Where(f => f.ShareStatus == FolderStatus.Approved)
            .OrderByDescending(f => f.SharedAt)
            .ThenBy(f => f.Name)
            .Select(f => new FolderDto
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                DocumentCount = f.Documents.Count,
                IsFavorite = f.IsFavorite,
                ShareStatus = f.ShareStatus,
                SharedAt = f.SharedAt,
                ShareReviewSource = f.ShareReviewSource,
                AiReviewReason = f.AiReviewReason,
                AiReviewConfidence = f.AiReviewConfidence,
                HumanReviewReason = f.HumanReviewReason,
                RequiresHumanReview = f.RequiresHumanReview,
                AppealRequestedAt = f.AppealRequestedAt,
                AppealMessage = f.AppealMessage,
                Icon = f.Icon,
                OwnerName = f.User.FullName ?? f.User.Username,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
                LikeCount = f.Reactions.Count(r => r.IsLike),
                DislikeCount = f.Reactions.Count(r => !r.IsLike),
                CurrentUserVote = currentProfileId.HasValue
                    ? f.Reactions
                        .Where(reaction => reaction.UserId == currentProfileId.Value)
                        .Select(reaction => (bool?)reaction.IsLike)
                        .FirstOrDefault()
                    : null,
                Status = f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Rejected" :
                         f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                         f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready) ? (f.ShareStatus == FolderStatus.Approved ? "Shared" : "Pending Share") :
                         "Empty"
            })
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<IReadOnlyList<FolderDto>> ListPersonalSharedAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);

        var rows = await _db.Folders
            .AsNoTracking()
            .Where(f => f.UserId == profile.Id && f.ShareStatus != FolderStatus.None)
            .OrderByDescending(f => f.SharedAt)
            .ThenBy(f => f.Name)
            .Select(f => new FolderDto
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                DocumentCount = f.Documents.Count,
                IsFavorite = f.IsFavorite,
                ShareStatus = f.ShareStatus,
                SharedAt = f.SharedAt,
                ShareReviewSource = f.ShareReviewSource,
                AiReviewReason = f.AiReviewReason,
                AiReviewConfidence = f.AiReviewConfidence,
                HumanReviewReason = f.HumanReviewReason,
                RequiresHumanReview = f.RequiresHumanReview,
                AppealRequestedAt = f.AppealRequestedAt,
                AppealMessage = f.AppealMessage,
                Icon = f.Icon,
                OwnerName = f.User.FullName ?? f.User.Username,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
                LikeCount = f.Reactions.Count(r => r.IsLike),
                DislikeCount = f.Reactions.Count(r => !r.IsLike),
                Status = f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Rejected" :
                         f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                         f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready) ? (f.ShareStatus == FolderStatus.Approved ? "Shared" : "Pending Share") :
                         "Empty"
            })
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<FolderDto> RequestShareAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist or does not belong to the caller.");

        if (folder.ShareStatus != FolderStatus.None && folder.ShareStatus != FolderStatus.Rejected)
        {
            throw new DocumentException(400, "invalid_share_status",
                "Only folders with status None or Rejected can be requested for sharing.");
        }

        var decision = _shareAiModerator.Evaluate(folder, folder.Documents.ToList());
        var now = DateTimeOffset.UtcNow;

        folder.ShareReviewSource = "AI";
        folder.AiReviewReason = decision.Reason;
        folder.AiReviewConfidence = decision.Confidence;
        folder.HumanReviewReason = null;
        folder.AppealRequestedAt = null;
        folder.AppealMessage = null;

        switch (decision.Outcome)
        {
            case FolderShareModerationOutcome.AutoApproved:
                folder.ShareStatus = FolderStatus.Approved;
                folder.SharedAt = now;
                folder.RequiresHumanReview = false;
                break;
            case FolderShareModerationOutcome.AutoRejected:
                folder.ShareStatus = FolderStatus.Rejected;
                folder.SharedAt = null;
                folder.RequiresHumanReview = false;
                break;
            default:
                folder.ShareStatus = FolderStatus.PendingShare;
                folder.SharedAt = null;
                folder.RequiresHumanReview = true;
                break;
        }

        folder.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task<FolderDto> AppealShareReviewAsync(
        Guid supabaseUserId,
        Guid folderId,
        AppealFolderShareRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist or does not belong to the caller.");

        if (folder.ShareStatus != FolderStatus.Rejected)
        {
            throw new DocumentException(400, "appeal_not_allowed",
                "Only AI-rejected folders can request human review.");
        }

        folder.ShareStatus = FolderStatus.PendingShare;
        folder.RequiresHumanReview = true;
        folder.AppealRequestedAt = DateTimeOffset.UtcNow;
        folder.AppealMessage = NormalizeModerationNote(request.Message);
        folder.ShareReviewSource = "STUDENT_APPEAL";
        folder.HumanReviewReason = null;
        folder.SharedAt = null;
        folder.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task<FolderDto> ApproveFolderShareAsync(
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist.");

        if (folder.ShareStatus != FolderStatus.PendingShare)
        {
            throw new DocumentException(400, "invalid_share_status",
                "Only folders with status Pending Share can be approved.");
        }

        folder.ShareStatus = FolderStatus.Approved;
        folder.SharedAt = DateTimeOffset.UtcNow;
        folder.ShareReviewSource = "HUMAN";
        folder.HumanReviewReason = "Approved after moderator review.";
        folder.RequiresHumanReview = false;
        folder.AppealRequestedAt = null;
        folder.AppealMessage = null;
        folder.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task<FolderDto> RejectFolderShareAsync(
        Guid folderId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist.");

        if (folder.ShareStatus != FolderStatus.PendingShare)
        {
            throw new DocumentException(400, "invalid_share_status",
                "Only folders with status Pending Share can be rejected.");
        }

        folder.ShareStatus = FolderStatus.Rejected;
        folder.SharedAt = null;
        folder.ShareReviewSource = "HUMAN";
        folder.HumanReviewReason = NormalizeModerationNote(reason) ?? "Rejected after moderator review.";
        folder.RequiresHumanReview = false;
        folder.AppealRequestedAt = null;
        folder.AppealMessage = null;
        folder.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task<FolderDto> VoteAsync(
        Guid supabaseUserId,
        Guid folderId,
        bool isLike,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var folder = await _db.Folders
            .Include(f => f.User)
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.ShareStatus == FolderStatus.Approved, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found", "Folder not found.");

        var existing = await _db.FolderReactions
            .FirstOrDefaultAsync(r => r.FolderId == folderId && r.UserId == profile.Id, cancellationToken);

        if (existing is not null)
        {
            if (existing.IsLike == isLike)
            {
                _db.FolderReactions.Remove(existing);
            }
            else
            {
                existing.IsLike = isLike;
            }
        }
        else
        {
            _db.FolderReactions.Add(new FolderReaction
            {
                FolderId = folderId,
                UserId = profile.Id,
                IsLike = isLike,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var likeCount = await _db.FolderReactions.CountAsync(r => r.FolderId == folderId && r.IsLike, cancellationToken);
        var dislikeCount = await _db.FolderReactions.CountAsync(r => r.FolderId == folderId && !r.IsLike, cancellationToken);
        var currentVote = await _db.FolderReactions
            .Where(r => r.FolderId == folderId && r.UserId == profile.Id)
            .Select(r => (bool?)r.IsLike)
            .FirstOrDefaultAsync(cancellationToken);
        var docCount = await _db.Documents.CountAsync(d => d.FolderId == folderId, cancellationToken);

        return new FolderDto
        {
            Id = folder.Id,
            Name = folder.Name,
            Description = folder.Description,
            DocumentCount = docCount,
            IsFavorite = folder.IsFavorite,
            ShareStatus = folder.ShareStatus,
            SharedAt = folder.SharedAt,
            ShareReviewSource = folder.ShareReviewSource,
            AiReviewReason = folder.AiReviewReason,
            AiReviewConfidence = folder.AiReviewConfidence,
            HumanReviewReason = folder.HumanReviewReason,
            RequiresHumanReview = folder.RequiresHumanReview,
            AppealRequestedAt = folder.AppealRequestedAt,
            AppealMessage = folder.AppealMessage,
            Icon = folder.Icon,
            OwnerName = folder.User.FullName ?? folder.User.Username,
            CreatedAt = folder.CreatedAt,
            UpdatedAt = folder.UpdatedAt,
            LikeCount = likeCount,
            DislikeCount = dislikeCount,
            CurrentUserVote = currentVote,
            Status = folder.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Rejected" :
                     folder.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                     folder.Documents.Any() && folder.Documents.All(d => d.Status == DocumentStatus.Ready) ? (folder.ShareStatus == FolderStatus.Approved ? "Shared" : "Pending Share") :
                     "Empty"
        };
    }

    public async Task<FolderDto> CopySharedFolderAsync(
        Guid supabaseUserId,
        Guid sharedFolderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);

        var source = await _db.Folders
            .AsNoTracking()
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == sharedFolderId && f.ShareStatus == FolderStatus.Approved, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Shared folder not found.");

        var chunksByDocumentId = new Dictionary<Guid, List<DocumentChunk>>();
        if (source.Documents.Count > 0
            && _db.Model.FindEntityType(typeof(DocumentChunk)) is not null)
        {
            var documentIds = source.Documents.Select(document => document.Id).ToList();
            var chunks = await _db.DocumentChunks
                .AsNoTracking()
                .Where(chunk => documentIds.Contains(chunk.DocumentId))
                .OrderBy(chunk => chunk.DocumentId)
                .ThenBy(chunk => chunk.ChunkIndex)
                .ToListAsync(cancellationToken);
            chunksByDocumentId = chunks
                .GroupBy(chunk => chunk.DocumentId)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        var now = DateTimeOffset.UtcNow;
        var name = await BuildUniqueCopyNameAsync(
            profile.Id,
            source.Name,
            cancellationToken);

        var newFolder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = profile.Id,
            Name = name,
            Description = source.Description,
            Icon = source.Icon,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Folders.Add(newFolder);

        var uploadedPaths = new List<string>();
        try
        {
            foreach (var doc in source.Documents)
            {
                var documentId = Guid.NewGuid();
                var slug = SanitizeFileName(doc.FileName);
                var newStoragePath = $"users/{profile.Id:N}/{now.Year}/{documentId:N}-{slug}";

                await CopyStorageFileAsync(doc.StoragePath, newStoragePath, cancellationToken);
                uploadedPaths.Add(newStoragePath);

                var newDoc = new Document
                {
                    Id = documentId,
                    UserId = profile.Id,
                    FolderId = newFolder.Id,
                    FileName = doc.FileName,
                    StoragePath = newStoragePath,
                    FileSizeBytes = doc.FileSizeBytes,
                    MimeType = doc.MimeType,
                    SubjectCode = doc.SubjectCode,
                    Semester = doc.Semester,
                    PageCount = doc.PageCount,
                    Status = doc.Status,
                    ErrorMessage = doc.ErrorMessage,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.Documents.Add(newDoc);

                foreach (var chunk in chunksByDocumentId.GetValueOrDefault(doc.Id) ?? [])
                {
                    _db.DocumentChunks.Add(new DocumentChunk
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = documentId,
                        ChunkIndex = chunk.ChunkIndex,
                        PageNumber = chunk.PageNumber,
                        Content = chunk.Content,
                        TokenCount = chunk.TokenCount,
                        Embedding = chunk.Embedding,
                        CreatedAt = now,
                    });
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            foreach (var path in uploadedPaths)
            {
                try
                {
                    await _storage.DeleteAsync(DocumentService.BucketName, path, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "Failed to clean copied storage object {StoragePath}.",
                        path);
                }
            }

            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogError(ex, "Failed to save shared folder {FolderId} for user {UserId}.", source.Id, profile.Id);
            if (ex is DocumentException)
            {
                throw;
            }
            throw new DocumentException(502, "folder_copy_failed",
                "The shared folder could not be copied safely. No library record was created.");
        }

        _logger.LogInformation("Folder copied: sharedFolderId={SourceId} newFolderId={NewId} user={UserId} name={Name} documents={DocCount}",
            sharedFolderId, newFolder.Id, profile.Id, newFolder.Name, source.Documents.Count);

        return new FolderDto
        {
            Id = newFolder.Id,
            Name = newFolder.Name,
            Description = newFolder.Description,
            DocumentCount = source.Documents.Count,
            IsFavorite = false,
            ShareStatus = FolderStatus.None,
            Icon = source.Icon,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task CopyStorageFileAsync(string sourcePath, string destPath, CancellationToken ct)
    {
        var (stream, contentType) = await _storage.DownloadFileAsync(
            DocumentService.BucketName,
            sourcePath,
            ct);
        await using (stream)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }
            await _storage.UploadAsync(
                DocumentService.BucketName,
                destPath,
                stream,
                contentType,
                upsert: false,
                ct);
        }
    }

    private async Task<string> BuildUniqueCopyNameAsync(
        Guid userId,
        string sourceName,
        CancellationToken cancellationToken)
    {
        var existingNames = await _db.Folders
            .AsNoTracking()
            .Where(folder => folder.UserId == userId)
            .Select(folder => folder.Name)
            .ToListAsync(cancellationToken);
        var used = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(sourceName))
        {
            return sourceName;
        }

        for (var suffix = 1; suffix < 10_000; suffix++)
        {
            var suffixText = $" ({suffix})";
            var maxBaseLength = Math.Max(1, 100 - suffixText.Length);
            var baseName = sourceName.Length > maxBaseLength
                ? sourceName[..maxBaseLength]
                : sourceName;
            var candidate = baseName + suffixText;
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new DocumentException(409, "folder_name_conflict",
            "Could not create a unique name for the saved folder.");
    }

    private static string SanitizeFileName(string fileName)
    {
        var trimmed = Path.GetFileName(fileName).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return "upload.bin";
        var safe = new string(trimmed.Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
        return safe.Length > 80 ? safe[..80] : safe;
    }

    private async Task<User> ResolveProfileAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        if (!profile.IsActive)
        {
            throw new DocumentException(403, "user_inactive",
                "User account is inactive and cannot manage folders.");
        }

        return profile;
    }

    private async Task EnsureUniqueNameAsync(
        Guid userId,
        string name,
        Guid? excludeFolderId,
        CancellationToken cancellationToken)
    {
        var normalized = name.ToUpperInvariant();
        var exists = await _db.Folders
            .AsNoTracking()
            .AnyAsync(f => f.UserId == userId
                && f.Name.ToUpper() == normalized
                && (!excludeFolderId.HasValue || f.Id != excludeFolderId.Value), cancellationToken);

        if (exists)
        {
            throw new DocumentException(409, "folder_name_taken",
                "You already have a folder with this name.");
        }
    }

    private static string NormalizeName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            throw new DocumentException(400, "folder_name_required", "Folder name is required.");
        }
        if (normalized.Length > 100)
        {
            throw new DocumentException(400, "folder_name_too_long", "Folder name must be 100 characters or fewer.");
        }
        return normalized;
    }

    private static string? NormalizeDescription(string? description)
    {
        var normalized = description?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeModerationNote(string? note)
    {
        var normalized = note?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= 2000 ? normalized : normalized[..2000];
    }

    private static FolderDto ToDto(Folder folder, int documentCount) => new()
    {
        Id = folder.Id,
        Name = folder.Name,
        Description = folder.Description,
        DocumentCount = documentCount,
        IsFavorite = folder.IsFavorite,
        ShareStatus = folder.ShareStatus,
        SharedAt = folder.SharedAt,
        ShareReviewSource = folder.ShareReviewSource,
        AiReviewReason = folder.AiReviewReason,
        AiReviewConfidence = folder.AiReviewConfidence,
        HumanReviewReason = folder.HumanReviewReason,
        RequiresHumanReview = folder.RequiresHumanReview,
        AppealRequestedAt = folder.AppealRequestedAt,
        AppealMessage = folder.AppealMessage,
        Icon = folder.Icon,
        CreatedAt = folder.CreatedAt,
        UpdatedAt = folder.UpdatedAt,
        Status = folder.Documents == null ? "Empty" :
                 folder.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Rejected" :
                 folder.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                 folder.Documents.Any() && folder.Documents.All(d => d.Status == DocumentStatus.Ready) ? (folder.ShareStatus == FolderStatus.Approved ? "Shared" : "Pending Share") :
                 "Empty"
    };
}

