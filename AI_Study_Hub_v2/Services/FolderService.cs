using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace AI_Study_Hub_v2.Services;

public sealed class FolderService : IFolderService
{
    private readonly AppDbContext _db;
    private readonly ILogger<FolderService> _logger;
    private readonly IStorageDeletionCoordinator _deletionCoordinator;
    private readonly IFolderShareAiModerator _shareAiModerator;
    private readonly IPlanCapacityGuard _capacityGuard;
    private readonly ISharedFolderCopyCoordinator _copyCoordinator;
    private readonly IAuditLogService _audit;

    public FolderService(
        AppDbContext db,
        ILogger<FolderService> logger,
        IStorageDeletionCoordinator deletionCoordinator,
        IFolderShareAiModerator shareAiModerator,
        IPlanCapacityGuard capacityGuard,
        ISharedFolderCopyCoordinator copyCoordinator,
        IAuditLogService audit)
    {
        _db = db;
        _logger = logger;
        _deletionCoordinator = deletionCoordinator;
        _shareAiModerator = shareAiModerator;
        _capacityGuard = capacityGuard;
        _copyCoordinator = copyCoordinator;
        _audit = audit;
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
                AiReviewFailureCount = f.AiReviewFailureCount,
                HumanReviewReason = f.HumanReviewReason,
                RequiresHumanReview = f.RequiresHumanReview,
                AppealRequestedAt = f.AppealRequestedAt,
                AppealMessage = f.AppealMessage,
                Icon = f.Icon,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
                Status = f.Documents.Count == 0 ? "Empty" :
                         f.ShareStatus == FolderStatus.Rejected ? "Rejected" :
                         f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Failed" :
                         f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                         f.ShareStatus == FolderStatus.PendingShare ? "Pending Share" :
                         f.ShareStatus == FolderStatus.Approved ? "Shared" :
                         "Private"
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

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await _capacityGuard.LockAndValidateAsync(_db, profile.Id, new PlanCapacityRequest(0, 1, null, 0), cancellationToken);
            await EnsureUniqueNameAsync(profile.Id, name, excludeFolderId: null, cancellationToken);
            _db.Folders.Add(folder);
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await tx.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                _logger.LogError(rollbackException, "Folder creation rollback failed.");
            }
            throw;
        }

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
        if (!await _deletionCoordinator.DeleteOwnedFolderAsync(folderId, profile.Id, cancellationToken))
        {
            throw new DocumentException(404, "folder_not_found", "Folder does not exist or does not belong to the caller.");
        }
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
                AiReviewFailureCount = f.AiReviewFailureCount,
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
                Status = f.Documents.Count == 0 ? "Empty" :
                         f.ShareStatus == FolderStatus.Rejected ? "Rejected" :
                         f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Failed" :
                         f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                         f.ShareStatus == FolderStatus.PendingShare ? "Pending Share" :
                         f.ShareStatus == FolderStatus.Approved ? "Shared" :
                         "Private"
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
                AiReviewFailureCount = f.AiReviewFailureCount,
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
                Status = f.Documents.Count == 0 ? "Empty" :
                         f.ShareStatus == FolderStatus.Rejected ? "Rejected" :
                         f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Failed" :
                         f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                         f.ShareStatus == FolderStatus.PendingShare ? "Pending Share" :
                         f.ShareStatus == FolderStatus.Approved ? "Shared" :
                         "Private"
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

        if (!folder.Documents.Any())
        {
            throw new DocumentException(400, "empty_folder",
                "Folder has no documents, so it cannot be shared to the community yet.");
        }

        if (folder.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing))
        {
            throw new DocumentException(400, "folder_has_processing_documents",
                "Cannot share this folder because some documents are still processing or uploading. Please wait until they are finished.");
        }

        if (folder.Documents.Any(d => d.Status == DocumentStatus.Failed))
        {
            throw new DocumentException(400, "folder_has_failed_documents",
                "Cannot share this folder because it contains documents that failed to process. Please remove or re-upload the failed documents before sharing.");
        }

        var documentIds = folder.Documents.Select(document => document.Id).ToList();
        IReadOnlyList<string> extractedTexts = Array.Empty<string>();
        if (documentIds.Count > 0)
        {
            try
            {
                var chunkRows = await _db.DocumentChunks
                    .Where(chunk => documentIds.Contains(chunk.DocumentId))
                    .OrderBy(chunk => chunk.DocumentId)
                    .ThenBy(chunk => chunk.ChunkIndex)
                    .Select(chunk => new { chunk.DocumentId, chunk.Content })
                    .ToListAsync(cancellationToken);

                extractedTexts = chunkRows
                    .Select(row => row.Content)
                    .Take(24)
                    .ToList();
            }
            catch (InvalidOperationException)
            {
                extractedTexts = Array.Empty<string>();
            }
        }

        var decision = _shareAiModerator.Evaluate(folder, folder.Documents.ToList(), extractedTexts);

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
                folder.AiReviewFailureCount = 0;
                folder.RequiresHumanReview = false;
                break;
            default:
                folder.ShareStatus = FolderStatus.Rejected;
                folder.SharedAt = null;
                folder.AiReviewFailureCount += 1;
                folder.RequiresHumanReview = folder.AiReviewFailureCount >= 2;
                break;
        }

        folder.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(supabaseUserId, "FOLDER_SHARE_REQUESTED", "Folder", folder.Id.ToString(), "Low",
            afterJson: JsonSerializer.Serialize(new { folder.Name, folder.ShareStatus }));

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

        if (folder.ShareStatus != FolderStatus.Rejected || folder.AiReviewFailureCount < 2)
        {
            throw new DocumentException(400, "appeal_not_allowed",
                "Human review is available only after two unsuccessful AI reviews.");
        }

        folder.ShareStatus = FolderStatus.PendingShare;
        folder.RequiresHumanReview = true;
        folder.AppealRequestedAt = DateTimeOffset.UtcNow;
        folder.AppealMessage = NormalizeModerationNote(request.Message);
        folder.ShareReviewSource = "HUMAN_REQUEST";
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

        var now = DateTimeOffset.UtcNow;
        folder.ShareStatus = FolderStatus.Approved;
        folder.SharedAt = now;
        folder.ShareReviewSource = "HUMAN";
        folder.HumanReviewReason = "Approved after moderator review.";
        folder.RequiresHumanReview = false;
        folder.AppealRequestedAt = null;
        folder.AppealMessage = null;
        folder.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(null, "FOLDER_SHARE_APPROVED", "Folder", folderId.ToString(), "Medium",
            afterJson: JsonSerializer.Serialize(new { folder.Name, folder.ShareStatus }));

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task<FolderDto> RejectFolderShareAsync(
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
                "Only folders with status Pending Share can be rejected.");
        }

        var now = DateTimeOffset.UtcNow;
        folder.ShareStatus = FolderStatus.Rejected;
        folder.SharedAt = null;
        folder.ShareReviewSource = "HUMAN";
        folder.HumanReviewReason = "Rejected after moderator review.";
        folder.RequiresHumanReview = false;
        folder.AppealRequestedAt = null;
        folder.AppealMessage = null;
        folder.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(null, "FOLDER_SHARE_REJECTED", "Folder", folderId.ToString(), "Medium",
            afterJson: JsonSerializer.Serialize(new { folder.Name, folder.ShareStatus }));

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
            AiReviewFailureCount = folder.AiReviewFailureCount,
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
            Status = MapFolderStatus(folder.ShareStatus, folder.Documents)
        };
    }

    public async Task<FolderDto> GetFolderAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        if (!profile.IsActive)
        {
            throw new DocumentException(403, "user_inactive",
                "User account is inactive.");
        }

        var folder = await _db.Folders
            .AsNoTracking()
            .Include(f => f.User)
            .Include(f => f.Documents)
            .Include(f => f.Reactions)
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found", "Folder not found.");

        var isOwner = folder.UserId == profile.Id;
        var isApproved = folder.ShareStatus == FolderStatus.Approved;
        var roleName = profile.Role?.RoleName ?? string.Empty;
        var isPrivileged = roleName.Equals(Role.AdminRoleName, StringComparison.OrdinalIgnoreCase)
                        || roleName.Equals(Role.ModeratorRoleName, StringComparison.OrdinalIgnoreCase);

        if (!isOwner && !isApproved && !isPrivileged)
        {
            throw new DocumentException(403, "folder_access_denied",
                "You do not have permission to access this folder.");
        }

        var likeCount = folder.Reactions.Count(r => r.IsLike);
        var dislikeCount = folder.Reactions.Count(r => !r.IsLike);
        var currentVote = folder.Reactions
            .Where(r => r.UserId == profile.Id)
            .Select(r => (bool?)r.IsLike)
            .FirstOrDefault();

        return new FolderDto
        {
            Id = folder.Id,
            Name = folder.Name,
            Description = folder.Description,
            DocumentCount = folder.Documents.Count,
            IsFavorite = folder.IsFavorite,
            ShareStatus = folder.ShareStatus,
            SharedAt = folder.SharedAt,
            ShareReviewSource = folder.ShareReviewSource,
            AiReviewReason = folder.AiReviewReason,
            AiReviewConfidence = folder.AiReviewConfidence,
            AiReviewFailureCount = folder.AiReviewFailureCount,
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
            Status = MapFolderStatus(folder.ShareStatus, folder.Documents)
        };
    }

    public async Task<FolderDto> CopySharedFolderAsync(Guid supabaseUserId, Guid sharedFolderId, CancellationToken cancellationToken = default)
    {
        var result = await _copyCoordinator.CopyAsync(supabaseUserId, sharedFolderId, cancellationToken);

        _audit.Add(supabaseUserId, "FOLDER_COPIED", "Folder", sharedFolderId.ToString(), "Low",
            afterJson: JsonSerializer.Serialize(new { result.Name, SourceFolderId = sharedFolderId }));

        return result;
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

        return normalized.Length <= 2000
            ? normalized
            : normalized[..2000];
    }

    private static string MapFolderStatus(FolderStatus shareStatus, ICollection<Document>? documents)
    {
        if (documents == null || documents.Count == 0)
        {
            return "Empty";
        }

        if (shareStatus == FolderStatus.Rejected)
        {
            return "Rejected";
        }

        if (documents.Any(d => d.Status == DocumentStatus.Failed))
        {
            return "Failed";
        }

        if (documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing))
        {
            return "Processing";
        }

        return shareStatus switch
        {
            FolderStatus.PendingShare => "Pending Share",
            FolderStatus.Approved => "Shared",
            _ => "Private"
        };
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
        AiReviewFailureCount = folder.AiReviewFailureCount,
        HumanReviewReason = folder.HumanReviewReason,
        RequiresHumanReview = folder.RequiresHumanReview,
        AppealRequestedAt = folder.AppealRequestedAt,
        AppealMessage = folder.AppealMessage,
        Icon = folder.Icon,
        CreatedAt = folder.CreatedAt,
        UpdatedAt = folder.UpdatedAt,
        Status = MapFolderStatus(folder.ShareStatus, folder.Documents)
    };
}

