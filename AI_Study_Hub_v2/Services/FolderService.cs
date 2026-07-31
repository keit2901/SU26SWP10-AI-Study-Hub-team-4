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
    private readonly IAuditLogService _audit;
    private readonly IFolderShareAiModerator _shareAiModerator;
    private readonly IPlanCapacityGuard _capacityGuard;
    private readonly ISharedFolderCopyCoordinator _copyCoordinator;
    private readonly IUserNotificationService _notifications;

    public FolderService(
        AppDbContext db,
        ILogger<FolderService> logger,
        IStorageDeletionCoordinator deletionCoordinator,
        IAuditLogService audit,
        IFolderShareAiModerator shareAiModerator,
        IPlanCapacityGuard capacityGuard,
        ISharedFolderCopyCoordinator copyCoordinator,
        IUserNotificationService notifications)
    {
        _db = db;
        _logger = logger;
        _deletionCoordinator = deletionCoordinator;
        _audit = audit;
        _shareAiModerator = shareAiModerator;
        _capacityGuard = capacityGuard;
        _copyCoordinator = copyCoordinator;
        _notifications = notifications;
    }

    public async Task<IReadOnlyList<FolderDto>> ListAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);

        var query = _db.Folders
            .AsNoTracking()
            .Where(f => f.UserId == profile.Id)
            .OrderByDescending(f => f.IsFavorite)
            .ThenByDescending(f => f.UpdatedAt);

        List<FolderDto> rows;
        if (schema.HasFullModernShareFlowColumns)
        {
            rows = await query
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
                    ShareSubmissionCount = f.ShareSubmissionCount,
                    ShareFailureCount = f.ShareFailureCount,
                    HumanReviewReason = f.HumanReviewReason,
                    StudentFeedbackReason = f.StudentFeedbackReason,
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
        }
        else
        {
            if (schema.HasShareSubmissionCountColumn)
            {
                rows = await query
                    .Select(f => new FolderDto
                    {
                        Id = f.Id,
                        Name = f.Name,
                        Description = f.Description,
                        DocumentCount = f.Documents.Count,
                        IsFavorite = f.IsFavorite,
                        ShareStatus = f.ShareStatus,
                        SharedAt = f.SharedAt,
                        ShareSubmissionCount = f.ShareSubmissionCount,
                        ShareFailureCount = f.ShareFailureCount,
                        Icon = f.Icon,
                        CreatedAt = f.CreatedAt,
                        UpdatedAt = f.UpdatedAt,
                        Status = f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Rejected" :
                                 f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                                 f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready) ? (f.ShareStatus == FolderStatus.Approved ? "Shared" : "Pending Share") :
                                 "Empty"
                    })
                    .ToListAsync(cancellationToken);
            }
            else
            {
                rows = await query
                    .Select(f => new FolderDto
                    {
                        Id = f.Id,
                        Name = f.Name,
                        Description = f.Description,
                        DocumentCount = f.Documents.Count,
                        IsFavorite = f.IsFavorite,
                        ShareStatus = f.ShareStatus,
                        SharedAt = f.SharedAt,
                        Icon = f.Icon,
                        CreatedAt = f.CreatedAt,
                        UpdatedAt = f.UpdatedAt,
                        Status = f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Rejected" :
                                 f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                                 f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready) ? (f.ShareStatus == FolderStatus.Approved ? "Shared" : "Pending Share") :
                                 "Empty"
                    })
                    .ToListAsync(cancellationToken);
            }

            _logger.LogWarning("Folder share-review columns are not fully available in the current database schema. Falling back to compatibility mode for ListAsync.");
        }

        return rows;
    }

    public async Task<FolderDto> CreateAsync(
        Guid supabaseUserId,
        CreateFolderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);
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

            if (schema.HasFullModernShareFlowColumns)
            {
                _db.Folders.Add(folder);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO folders (id, user_id, name, description, is_favorite, share_status, created_at, updated_at)
                    VALUES ({folder.Id}, {folder.UserId}, {folder.Name}, {folder.Description}, {false}, {(int)FolderStatus.None}, {folder.CreatedAt}, {folder.UpdatedAt})
                    """, cancellationToken);

                folder.IsFavorite = false;
                folder.ShareStatus = FolderStatus.None;
                _logger.LogWarning("Folder share-review columns are not available in the current database schema. Using compatibility insert mode for folder creation.");
            }

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
        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);

        if (!schema.HasFullModernShareFlowColumns)
        {
            var basicFolder = await _db.Folders
                .AsNoTracking()
                .Where(f => f.Id == folderId && f.UserId == profile.Id)
                .Select(f => new
                {
                    f.Id,
                    f.UserId,
                    f.Name,
                    f.Description,
                    f.Icon,
                    f.IsFavorite,
                    f.ShareStatus,
                    f.SharedAt,
                    f.CreatedAt,
                    f.UpdatedAt
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DocumentException(404, "folder_not_found",
                    "Folder does not exist or does not belong to the caller.");

            var updatedName = request.Name is not null ? NormalizeName(request.Name) : basicFolder.Name;
            if (request.Name is not null)
            {
                await EnsureUniqueNameAsync(profile.Id, updatedName, excludeFolderId: folderId, cancellationToken);
            }

            var updatedDescription = request.Description is not null
                ? NormalizeDescription(request.Description)
                : basicFolder.Description;

            var updatedIcon = request.Icon is not null
                ? (string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim())
                : basicFolder.Icon;

            var updatedFavorite = request.IsFavorite ?? basicFolder.IsFavorite;
            var updatedAt = DateTimeOffset.UtcNow;

            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE folders
                SET name = {updatedName},
                    description = {updatedDescription},
                    icon = {updatedIcon},
                    is_favorite = {updatedFavorite},
                    updated_at = {updatedAt}
                WHERE id = {folderId} AND user_id = {profile.Id}
                """, cancellationToken);

            _logger.LogWarning("Folder share-review columns are not available in the current database schema. Using compatibility update mode for folder edits.");

            var countCompat = await _db.Documents.CountAsync(d => d.FolderId == folderId, cancellationToken);
            return new FolderDto
            {
                Id = basicFolder.Id,
                Name = updatedName,
                Description = updatedDescription,
                Icon = updatedIcon,
                IsFavorite = updatedFavorite,
                ShareStatus = basicFolder.ShareStatus,
                SharedAt = basicFolder.SharedAt,
                CreatedAt = basicFolder.CreatedAt,
                UpdatedAt = updatedAt,
                DocumentCount = countCompat,
                Status = "Empty"
            };
        }

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
        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);

        if (!schema.HasFullModernShareFlowColumns)
        {
            var compatibilityFolder = await _db.Folders
                .AsNoTracking()
                .Where(f => f.Id == folderId && f.UserId == profile.Id)
                .Select(f => new
                {
                    f.Id,
                    f.Name,
                    f.Description,
                    f.Icon,
                    f.IsFavorite,
                    f.ShareStatus,
                    f.SharedAt,
                    f.CreatedAt,
                    f.UpdatedAt
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DocumentException(404, "folder_not_found",
                    "Folder does not exist or does not belong to the caller.");

            var newFavorite = !compatibilityFolder.IsFavorite;
            var updatedAt = DateTimeOffset.UtcNow;
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE folders
                SET is_favorite = {newFavorite},
                    updated_at = {updatedAt}
                WHERE id = {folderId} AND user_id = {profile.Id}
                """, cancellationToken);

            _logger.LogWarning("Folder share-review columns are not available in the current database schema. Using compatibility toggle-favorite mode.");

            var countCompat = await _db.Documents.CountAsync(d => d.FolderId == folderId, cancellationToken);
            return new FolderDto
            {
                Id = compatibilityFolder.Id,
                Name = compatibilityFolder.Name,
                Description = compatibilityFolder.Description,
                Icon = compatibilityFolder.Icon,
                IsFavorite = newFavorite,
                ShareStatus = compatibilityFolder.ShareStatus,
                SharedAt = compatibilityFolder.SharedAt,
                CreatedAt = compatibilityFolder.CreatedAt,
                UpdatedAt = updatedAt,
                DocumentCount = countCompat,
                Status = "Empty"
            };
        }

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

        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);
        var query = _db.Folders
            .AsNoTracking()
            .Where(f => f.ShareStatus == FolderStatus.Approved)
            .OrderByDescending(f => f.SharedAt)
            .ThenBy(f => f.Name);

        List<FolderDto> rows;
        if (schema.HasFullModernShareFlowColumns)
        {
            rows = await query
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
                    Status = f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Rejected" :
                             f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                             f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready) ? (f.ShareStatus == FolderStatus.Approved ? "Shared" : "Pending Share") :
                             "Empty"
                })
                .ToListAsync(cancellationToken);
        }
        else
        {
            if (schema.HasShareSubmissionCountColumn)
            {
                rows = await query
                    .Select(f => new FolderDto
                    {
                        Id = f.Id,
                        Name = f.Name,
                        Description = f.Description,
                        DocumentCount = f.Documents.Count,
                        IsFavorite = f.IsFavorite,
                        ShareStatus = f.ShareStatus,
                        SharedAt = f.SharedAt,
                        ShareSubmissionCount = f.ShareSubmissionCount,
                        ShareFailureCount = f.ShareFailureCount,
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
            }
            else
            {
                rows = await query
                    .Select(f => new FolderDto
                    {
                        Id = f.Id,
                        Name = f.Name,
                        Description = f.Description,
                        DocumentCount = f.Documents.Count,
                        IsFavorite = f.IsFavorite,
                        ShareStatus = f.ShareStatus,
                        SharedAt = f.SharedAt,
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
            }

            _logger.LogWarning("Folder share-review columns are not fully available in the current database schema. Falling back to compatibility mode for ListSharedAsync.");
        }

        return rows;
    }

    public async Task<IReadOnlyList<FolderDto>> ListPersonalSharedAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);
        var query = _db.Folders
            .AsNoTracking()
            .Where(f => f.UserId == profile.Id && f.ShareStatus != FolderStatus.None)
            .OrderByDescending(f => f.UpdatedAt)
            .ThenBy(f => f.Name);

        List<FolderDto> rows;
        if (schema.HasFullModernShareFlowColumns)
        {
            rows = await query
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
                    ShareSubmissionCount = f.ShareSubmissionCount,
                    ShareFailureCount = f.ShareFailureCount,
                    HumanReviewReason = f.HumanReviewReason,
                    StudentFeedbackReason = f.StudentFeedbackReason,
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
                             f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready) ?
                                 (f.ShareStatus == FolderStatus.Approved ? "Shared" :
                                  f.ShareStatus == FolderStatus.Rejected ? "Rejected" : "Pending Share") :
                             "Empty"
                })
                .ToListAsync(cancellationToken);
        }
        else
        {
            if (schema.HasShareSubmissionCountColumn)
            {
                rows = await query
                    .Select(f => new FolderDto
                    {
                        Id = f.Id,
                        Name = f.Name,
                        Description = f.Description,
                        DocumentCount = f.Documents.Count,
                        IsFavorite = f.IsFavorite,
                        ShareStatus = f.ShareStatus,
                        SharedAt = f.SharedAt,
                        ShareSubmissionCount = f.ShareSubmissionCount,
                        ShareFailureCount = f.ShareFailureCount,
                        Icon = f.Icon,
                        OwnerName = f.User.FullName ?? f.User.Username,
                        CreatedAt = f.CreatedAt,
                        UpdatedAt = f.UpdatedAt,
                        LikeCount = f.Reactions.Count(r => r.IsLike),
                        DislikeCount = f.Reactions.Count(r => !r.IsLike),
                        Status = f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Rejected" :
                                 f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                                 f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready) ?
                                     (f.ShareStatus == FolderStatus.Approved ? "Shared" :
                                      f.ShareStatus == FolderStatus.Rejected ? "Rejected" : "Pending Share") :
                                 "Empty"
                    })
                    .ToListAsync(cancellationToken);
            }
            else
            {
                rows = await query
                    .Select(f => new FolderDto
                    {
                        Id = f.Id,
                        Name = f.Name,
                        Description = f.Description,
                        DocumentCount = f.Documents.Count,
                        IsFavorite = f.IsFavorite,
                        ShareStatus = f.ShareStatus,
                        SharedAt = f.SharedAt,
                        Icon = f.Icon,
                        OwnerName = f.User.FullName ?? f.User.Username,
                        CreatedAt = f.CreatedAt,
                        UpdatedAt = f.UpdatedAt,
                        LikeCount = f.Reactions.Count(r => r.IsLike),
                        DislikeCount = f.Reactions.Count(r => !r.IsLike),
                        Status = f.Documents.Any(d => d.Status == DocumentStatus.Failed) ? "Rejected" :
                                 f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing) ? "Processing" :
                                 f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready) ?
                                     (f.ShareStatus == FolderStatus.Approved ? "Shared" :
                                      f.ShareStatus == FolderStatus.Rejected ? "Rejected" : "Pending Share") :
                                 "Empty"
                    })
                    .ToListAsync(cancellationToken);
            }

            _logger.LogWarning("Folder share-review columns are not fully available in the current database schema. Falling back to compatibility mode for ListPersonalSharedAsync.");
        }

        return rows;
    }

    public async Task<FolderDto> RequestShareAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        const string manualSubmissionSource = "MANUAL_SUBMISSION";

        if (!schema.HasFullModernShareFlowColumns)
        {
            var folderInfo = await _db.Folders
                .AsNoTracking()
                .Where(f => f.Id == folderId && f.UserId == profile.Id)
                .Select(f => new
                {
                    f.Id,
                    f.Name,
                    f.Description,
                    f.IsFavorite,
                    f.ShareStatus,
                    f.SharedAt,
                    ShareSubmissionCount = schema.HasShareSubmissionCountColumn ? f.ShareSubmissionCount : 0,
                    ShareFailureCount = schema.HasShareFailureCountColumn ? f.ShareFailureCount : 0,
                    f.Icon,
                    f.CreatedAt,
                    f.UpdatedAt,
                    DocumentCount = f.Documents.Count
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DocumentException(404, "folder_not_found",
                    "Folder does not exist or does not belong to the caller.");

            if (folderInfo.ShareStatus != FolderStatus.None && folderInfo.ShareStatus != FolderStatus.Rejected)
            {
                throw new DocumentException(400, "invalid_share_status",
                    "Only folders with status None or Rejected can be requested for sharing.");
            }

            if (schema.HasShareSubmissionCountColumn)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET share_status = {(int)FolderStatus.PendingShare},
    shared_at = NULL,
    share_submission_count = share_submission_count + 1,
    updated_at = {now}
WHERE id = {folderId} AND user_id = {profile.Id}", cancellationToken);
            }
            else
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET share_status = {(int)FolderStatus.PendingShare},
    shared_at = NULL,
    updated_at = {now}
WHERE id = {folderId} AND user_id = {profile.Id}", cancellationToken);
            }

            await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE documents
SET review_status = {(int)DocumentReviewStatus.None},
    error_message = NULL,
    updated_at = {now}
WHERE folder_id = {folderId}", cancellationToken);

            if (schema.HasExtendedShareReviewColumns)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET share_review_source = {manualSubmissionSource},
    ai_review_reason = NULL,
    ai_review_confidence = NULL,
    ai_review_failure_count = 0,
    human_review_reason = NULL,
    requires_human_review = TRUE,
    appeal_requested_at = NULL,
    appeal_message = NULL
WHERE id = {folderId} AND user_id = {profile.Id}", cancellationToken);
            }

            if (schema.HasStudentFeedbackReasonColumn)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET student_feedback_reason = NULL
WHERE id = {folderId} AND user_id = {profile.Id}", cancellationToken);
            }

            _logger.LogWarning("Folder share-review columns are not fully available in the current database schema. RequestShareAsync is using compatibility updates.");

            _audit.Add(supabaseUserId, "FOLDER_SHARE_REQUESTED", "Folder", folderId.ToString(), "Low",
                afterJson: JsonSerializer.Serialize(new { folderInfo.Name, ShareStatus = FolderStatus.PendingShare }));

            return new FolderDto
            {
                Id = folderInfo.Id,
                Name = folderInfo.Name,
                Description = folderInfo.Description,
                DocumentCount = folderInfo.DocumentCount,
                IsFavorite = folderInfo.IsFavorite,
                ShareStatus = FolderStatus.PendingShare,
                SharedAt = null,
                ShareReviewSource = schema.HasExtendedShareReviewColumns ? manualSubmissionSource : null,
                AiReviewFailureCount = 0,
                ShareSubmissionCount = schema.HasShareSubmissionCountColumn ? folderInfo.ShareSubmissionCount + 1 : 0,
                ShareFailureCount = folderInfo.ShareFailureCount,
                RequiresHumanReview = schema.HasExtendedShareReviewColumns,
                Icon = folderInfo.Icon,
                CreatedAt = folderInfo.CreatedAt,
                UpdatedAt = now,
                Status = folderInfo.DocumentCount > 0 ? "Pending Share" : "Empty"
            };
        }

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

        folder.ShareStatus = FolderStatus.PendingShare;
        folder.SharedAt = null;
        folder.UpdatedAt = now;
        folder.ShareReviewSource = manualSubmissionSource;
        folder.AiReviewReason = null;
        folder.AiReviewConfidence = null;
        folder.AiReviewFailureCount = 0;
        folder.HumanReviewReason = null;
        folder.RequiresHumanReview = true;
        folder.AppealRequestedAt = null;
        folder.AppealMessage = null;
        folder.StudentFeedbackReason = null;
        folder.ShareSubmissionCount += 1;
        foreach (var document in folder.Documents)
        {
            document.ReviewStatus = DocumentReviewStatus.None;
            document.ErrorMessage = null;
            document.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(supabaseUserId, "FOLDER_SHARE_REQUESTED", "Folder", folder.Id.ToString(), "Low",
            afterJson: JsonSerializer.Serialize(new { folder.Name, folder.ShareStatus }));

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task<FolderDto> AutoCheckFolderShareAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveShareReviewerAsync(supabaseUserId, cancellationToken);
        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);
        if (!schema.HasFullModernShareFlowColumns)
        {
            var folderInfo = await _db.Folders
                .AsNoTracking()
                .Where(f => f.Id == folderId)
                .Select(f => new
                {
                    f.Id,
                    f.Name,
                    f.Description,
                    f.UserId,
                    f.IsFavorite,
                    f.ShareStatus,
                    f.SharedAt,
                    ShareSubmissionCount = schema.HasShareSubmissionCountColumn ? f.ShareSubmissionCount : 0,
                    ShareFailureCount = schema.HasShareFailureCountColumn ? f.ShareFailureCount : 0,
                    f.Icon,
                    f.CreatedAt,
                    f.UpdatedAt
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DocumentException(404, "folder_not_found",
                    "Folder does not exist.");

            EnsurePendingShare(folderInfo.ShareStatus, "auto-checked");

            var documents = await _db.Documents
                .AsNoTracking()
                .Where(d => d.FolderId == folderId)
                .ToListAsync(cancellationToken);

            var reviewFolder = new Folder
            {
                Id = folderInfo.Id,
                UserId = folderInfo.UserId,
                Name = folderInfo.Name,
                Description = folderInfo.Description,
                IsFavorite = folderInfo.IsFavorite,
                ShareStatus = folderInfo.ShareStatus,
                SharedAt = folderInfo.SharedAt,
                ShareSubmissionCount = folderInfo.ShareSubmissionCount,
                ShareFailureCount = folderInfo.ShareFailureCount,
                Icon = folderInfo.Icon,
                CreatedAt = folderInfo.CreatedAt,
                UpdatedAt = folderInfo.UpdatedAt
            };

            var decisionCompatibility = _shareAiModerator.Evaluate(
                reviewFolder,
                documents,
                await ExtractFolderTextsAsync(documents, cancellationToken));

            var nowCompatibility = DateTimeOffset.UtcNow;
            // Older schemas cannot retain the advisory fields, but AI must never publish or reject.
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET share_status = {(int)FolderStatus.Approved},
    shared_at = {nowCompatibility},
    updated_at = {nowCompatibility}
WHERE id = {folderId}", cancellationToken);

            var updatedDocumentCount = await _db.Documents.CountAsync(d => d.FolderId == folderId, cancellationToken);
            return new FolderDto
            {
                Id = folderInfo.Id,
                Name = folderInfo.Name,
                Description = folderInfo.Description,
                DocumentCount = updatedDocumentCount,
                IsFavorite = folderInfo.IsFavorite,
                ShareStatus = FolderStatus.Approved,
                SharedAt = nowCompatibility,
                ShareFailureCount = folderInfo.ShareFailureCount,
                Icon = folderInfo.Icon,
                CreatedAt = folderInfo.CreatedAt,
                UpdatedAt = nowCompatibility,
                Status = updatedDocumentCount > 0
                    ? "Pending Share"
                    : "Empty"
            };
        }

        var folder = await _db.Folders
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist.");

        EnsurePendingShare(folder.ShareStatus, "auto-checked");

        var decision = _shareAiModerator.Evaluate(
            folder,
            folder.Documents.ToList(),
            await ExtractFolderTextsAsync(folder.Documents, cancellationToken));

        ApplyAdvisoryShareDecision(folder, decision, DateTimeOffset.UtcNow);

        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(null, "FOLDER_SHARE_AUTO_CHECKED", "Folder", folderId.ToString(), "Medium",
            afterJson: JsonSerializer.Serialize(new
            {
                folder.Name,
                folder.ShareStatus,
                decision.Reason,
                decision.Confidence,
            }));
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
        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);

        var profile = await ResolveProfileAsync(supabaseUserId, cancellationToken);
        var feedbackReason = NormalizeRequiredShortText(
            request.Reason ?? request.Message,
            "feedback_reason_required",
            "Feedback reason is required.");
        var feedbackDescription = NormalizeRequiredLongText(
            request.Description ?? request.Message,
            "feedback_description_required",
            "Feedback description is required.");

        if (!schema.HasFullModernShareFlowColumns)
        {
            if (!schema.HasStudentFeedbackWorkflowColumns)
            {
                throw new DocumentException(503, "migration_required",
                    "Folder feedback is temporarily unavailable while the local database schema updates. Restart the app and try again.");
            }

            var folderInfo = await _db.Folders
                .AsNoTracking()
                .Where(f => f.Id == folderId && f.UserId == profile.Id)
                .Select(f => new
                {
                    f.Id,
                    f.Name,
                    f.Description,
                    f.IsFavorite,
                    f.ShareStatus,
                    ShareFailureCount = schema.HasShareFailureCountColumn ? f.ShareFailureCount : 0,
                    f.Icon,
                    f.CreatedAt
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DocumentException(404, "folder_not_found",
                    "Folder does not exist or does not belong to the caller.");

            if (folderInfo.ShareStatus != FolderStatus.Rejected || folderInfo.ShareFailureCount < 2)
            {
                throw new DocumentException(400, "appeal_not_allowed",
                    "Feedback is available only after two failed review attempts.");
            }

            var nowCompatibility = DateTimeOffset.UtcNow;
            var setClauses = new List<string>
            {
                "share_status = {0}",
                "shared_at = {1}",
                "updated_at = {2}",
                "student_feedback_reason = {3}",
                "appeal_message = {4}"
            };
            var sqlParameters = new List<object>
            {
                (int)FolderStatus.PendingShare,
                DBNull.Value,
                nowCompatibility,
                feedbackReason,
                feedbackDescription
            };

            if (schema.HasRequiresHumanReviewColumn)
            {
                setClauses.Add($"requires_human_review = {{{sqlParameters.Count}}}");
                sqlParameters.Add(true);
            }

            if (schema.HasAppealRequestedAtColumn)
            {
                setClauses.Add($"appeal_requested_at = {{{sqlParameters.Count}}}");
                sqlParameters.Add(nowCompatibility);
            }

            if (schema.HasShareReviewSourceColumn)
            {
                setClauses.Add($"share_review_source = {{{sqlParameters.Count}}}");
                sqlParameters.Add("STUDENT_FEEDBACK");
            }

            var folderIdParameterIndex = sqlParameters.Count;
            sqlParameters.Add(folderId);
            var profileIdParameterIndex = sqlParameters.Count;
            sqlParameters.Add(profile.Id);

            var updateSql = "UPDATE folders\nSET "
                + string.Join(",\n    ", setClauses)
                + $"\nWHERE id = {{{folderIdParameterIndex}}} AND user_id = {{{profileIdParameterIndex}}}";

            await _db.Database.ExecuteSqlRawAsync(updateSql,
                sqlParameters,
                cancellationToken);

            await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE documents
SET review_status = {(int)DocumentReviewStatus.None},
    error_message = NULL,
    updated_at = {nowCompatibility}
WHERE folder_id = {folderId}", cancellationToken);

            var countCompatibility = await _db.Documents.CountAsync(d => d.FolderId == folderId, cancellationToken);
            return new FolderDto
            {
                Id = folderInfo.Id,
                Name = folderInfo.Name,
                Description = folderInfo.Description,
                DocumentCount = countCompatibility,
                IsFavorite = folderInfo.IsFavorite,
                ShareStatus = FolderStatus.PendingShare,
                SharedAt = null,
                ShareReviewSource = schema.HasShareReviewSourceColumn ? "STUDENT_FEEDBACK" : null,
                ShareFailureCount = folderInfo.ShareFailureCount,
                StudentFeedbackReason = feedbackReason,
                AppealRequestedAt = schema.HasAppealRequestedAtColumn ? nowCompatibility : null,
                AppealMessage = feedbackDescription,
                RequiresHumanReview = schema.HasRequiresHumanReviewColumn,
                Icon = folderInfo.Icon,
                CreatedAt = folderInfo.CreatedAt,
                UpdatedAt = nowCompatibility,
                Status = countCompatibility > 0 ? "Pending Share" : "Empty"
            };
        }

        var folder = await _db.Folders
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == profile.Id, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist or does not belong to the caller.");

        if (folder.ShareStatus != FolderStatus.Rejected || folder.ShareFailureCount < 2)
        {
            throw new DocumentException(400, "appeal_not_allowed",
                "Feedback is available only after two failed review attempts.");
        }

        folder.ShareStatus = FolderStatus.PendingShare;
        folder.RequiresHumanReview = true;
        folder.AppealRequestedAt = DateTimeOffset.UtcNow;
        folder.AppealMessage = feedbackDescription;
        folder.StudentFeedbackReason = feedbackReason;
        folder.ShareReviewSource = "STUDENT_FEEDBACK";
        folder.SharedAt = null;
        folder.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var document in folder.Documents)
        {
            document.ReviewStatus = DocumentReviewStatus.None;
            document.ErrorMessage = null;
            document.UpdatedAt = folder.UpdatedAt;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task<FolderDto> ApproveFolderShareAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveShareReviewerAsync(supabaseUserId, cancellationToken);
        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);
        if (!schema.HasFullModernShareFlowColumns)
        {
            var folderInfo = await _db.Folders
                .AsNoTracking()
                .Where(f => f.Id == folderId)
                .Select(f => new
                {
                    f.Id,
                    f.UserId,
                    f.Name,
                    f.Description,
                    f.IsFavorite,
                    f.ShareStatus,
                    ShareSubmissionCount = schema.HasShareSubmissionCountColumn ? f.ShareSubmissionCount : 0,
                    f.Icon,
                    f.CreatedAt
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DocumentException(404, "folder_not_found",
                    "Folder does not exist.");

            EnsurePendingShare(folderInfo.ShareStatus, "approved");
            await EnsureFolderDocumentsApprovedAsync(folderId, cancellationToken);

            var nowCompatibility = DateTimeOffset.UtcNow;
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET share_status = {(int)FolderStatus.PendingShare},
    shared_at = NULL,
    updated_at = {nowCompatibility}
WHERE id = {folderId}", cancellationToken);

            var countCompatibility = await _db.Documents.CountAsync(d => d.FolderId == folderId, cancellationToken);
            return new FolderDto
            {
                Id = folderInfo.Id,
                Name = folderInfo.Name,
                Description = folderInfo.Description,
                DocumentCount = countCompatibility,
                IsFavorite = folderInfo.IsFavorite,
                ShareStatus = FolderStatus.PendingShare,
                SharedAt = null,
                Icon = folderInfo.Icon,
                CreatedAt = folderInfo.CreatedAt,
                UpdatedAt = nowCompatibility,
                Status = countCompatibility > 0 ? "Shared" : "Empty"
            };
        }

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist.");

        EnsurePendingShare(folder.ShareStatus, "approved");
        await EnsureFolderDocumentsApprovedAsync(folderId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var previousStatus = folder.ShareStatus;
        folder.ShareStatus = FolderStatus.Approved;
        folder.SharedAt = now;
        folder.UpdatedAt = now;

        if (schema.HasFullModernShareFlowColumns)
        {
            folder.ShareReviewSource = "HUMAN";
            folder.HumanReviewReason = null;
            folder.RequiresHumanReview = false;
            folder.AppealRequestedAt = null;
            folder.AppealMessage = null;
            folder.StudentFeedbackReason = null;
        }

        _notifications.StageFolderModerationFinal(folder, previousStatus, rejectionReason: null, now);
        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    public async Task<FolderDto> RejectFolderShareAsync(
        Guid supabaseUserId,
        Guid folderId,
        RejectFolderShareRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureActiveShareReviewerAsync(supabaseUserId, cancellationToken);
        var rejectionReason = NormalizeRequiredLongText(
            request.Reason,
            "reject_reason_required",
            "Reject reason is required.");
        var schema = await GetFolderSchemaCapabilitiesAsync(cancellationToken);
        if (!schema.HasFullModernShareFlowColumns)
        {
            var folderInfo = await _db.Folders
                .AsNoTracking()
                .Where(f => f.Id == folderId)
                .Select(f => new
                {
                    f.Id,
                    f.UserId,
                    f.Name,
                    f.Description,
                    f.IsFavorite,
                    f.ShareStatus,
                    ShareSubmissionCount = schema.HasShareSubmissionCountColumn ? f.ShareSubmissionCount : 0,
                    ShareFailureCount = schema.HasShareFailureCountColumn ? f.ShareFailureCount : 0,
                    f.Icon,
                    f.CreatedAt
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DocumentException(404, "folder_not_found",
                    "Folder does not exist.");

            EnsurePendingShare(folderInfo.ShareStatus, "rejected");

            if (!schema.HasShareSubmissionCountColumn)
            {
                throw new DocumentException(503, "notification_staging_requires_modern_schema",
                    "Folder moderation is temporarily unavailable while the notification migration updates the local schema.");
            }

            var nowCompatibility = DateTimeOffset.UtcNow;
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var updated = schema.HasShareFailureCountColumn
                    ? await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET share_status = {(int)FolderStatus.Rejected},
    shared_at = NULL,
    share_failure_count = share_failure_count + 1,
    updated_at = {nowCompatibility}
WHERE id = {folderId} AND share_status = {(int)FolderStatus.PendingShare}", cancellationToken)
                    : await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET share_status = {(int)FolderStatus.Rejected},
    shared_at = NULL,
    updated_at = {nowCompatibility}
WHERE id = {folderId} AND share_status = {(int)FolderStatus.PendingShare}", cancellationToken);

                if (updated != 1)
                {
                    throw new DocumentException(409, "folder_not_pending_share",
                        "Only folders with status Pending Share can be rejected.");
                }

                _notifications.StageFolderModerationFinal(new Folder
                {
                    Id = folderInfo.Id,
                    UserId = folderInfo.UserId,
                    Name = folderInfo.Name,
                    ShareStatus = FolderStatus.Rejected,
                    ShareSubmissionCount = folderInfo.ShareSubmissionCount
                }, FolderStatus.PendingShare, rejectionReason, nowCompatibility);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            var countCompatibility = await _db.Documents.CountAsync(d => d.FolderId == folderId, cancellationToken);
            return new FolderDto
            {
                Id = folderInfo.Id,
                Name = folderInfo.Name,
                Description = folderInfo.Description,
                DocumentCount = countCompatibility,
                IsFavorite = folderInfo.IsFavorite,
                ShareStatus = FolderStatus.Rejected,
                SharedAt = null,
                ShareSubmissionCount = folderInfo.ShareSubmissionCount,
                ShareFailureCount = schema.HasShareFailureCountColumn ? folderInfo.ShareFailureCount + 1 : 0,
                HumanReviewReason = rejectionReason,
                Icon = folderInfo.Icon,
                CreatedAt = folderInfo.CreatedAt,
                UpdatedAt = nowCompatibility,
                Status = countCompatibility > 0 ? "Rejected" : "Empty"
            };
        }

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new DocumentException(404, "folder_not_found",
                "Folder does not exist.");

        EnsurePendingShare(folder.ShareStatus, "rejected");

        var now = DateTimeOffset.UtcNow;
        var previousStatus = folder.ShareStatus;
        folder.ShareStatus = FolderStatus.Rejected;
        folder.SharedAt = null;
        folder.ShareReviewSource = "HUMAN";
        folder.HumanReviewReason = rejectionReason;
        folder.RequiresHumanReview = false;
        folder.AppealRequestedAt = null;
        folder.AppealMessage = null;
        folder.UpdatedAt = now;

        folder.ShareFailureCount += 1;

        _notifications.StageFolderModerationFinal(folder, previousStatus, rejectionReason, now);
        await _db.SaveChangesAsync(cancellationToken);

        var count = await _db.Documents.CountAsync(d => d.FolderId == folder.Id, cancellationToken);
        return ToDto(folder, count);
    }

    private async Task<IReadOnlyList<string>> ExtractFolderTextsAsync(
        IEnumerable<Document> documents,
        CancellationToken cancellationToken)
    {
        var documentIds = documents.Select(document => document.Id).ToList();
        if (documentIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        try
        {
            var chunkRows = await _db.DocumentChunks
                .Where(chunk => documentIds.Contains(chunk.DocumentId))
                .OrderBy(chunk => chunk.DocumentId)
                .ThenBy(chunk => chunk.ChunkIndex)
                .Select(chunk => chunk.Content)
                .Take(24)
                .ToListAsync(cancellationToken);

            return chunkRows;
        }
        catch (InvalidOperationException)
        {
            return Array.Empty<string>();
        }
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
            ShareSubmissionCount = folder.ShareSubmissionCount,
            ShareFailureCount = folder.ShareFailureCount,
            HumanReviewReason = folder.HumanReviewReason,
            StudentFeedbackReason = folder.StudentFeedbackReason,
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

    public Task<FolderDto> CopySharedFolderAsync(Guid supabaseUserId, Guid sharedFolderId, CancellationToken cancellationToken = default)
        => _copyCoordinator.CopyAsync(supabaseUserId, sharedFolderId, cancellationToken);

    private async Task<Guid> EnsureActiveShareReviewerAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new DocumentException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        if (!profile.IsActive)
        {
            throw new DocumentException(403, "user_inactive",
                "User account is inactive and cannot review folder shares.");
        }

        var roleName = await _db.Roles
            .AsNoTracking()
            .Where(role => role.Id == profile.RoleId)
            .Select(role => role.RoleName)
            .FirstOrDefaultAsync(cancellationToken);
        var isShareReviewer = string.Equals(roleName, Role.AdminRoleName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleName, Role.ModeratorRoleName, StringComparison.OrdinalIgnoreCase);
        if (!isShareReviewer)
        {
            throw new DocumentException(403, "share_reviewer_role_required",
                "Only active Admin or Moderator profiles can review folder shares.");
        }

        return profile.Id;
    }

    private static void EnsurePendingShare(FolderStatus shareStatus, string action)
    {
        if (shareStatus != FolderStatus.PendingShare)
        {
            throw new DocumentException(409, "folder_not_pending_share",
                $"Only folders with status Pending Share can be {action}.");
        }
    }

    private static void ApplyAdvisoryShareDecision(
        Folder folder,
        FolderShareModerationDecision decision,
        DateTimeOffset now)
    {
        folder.ShareStatus = FolderStatus.PendingShare;
        folder.SharedAt = null;
        folder.UpdatedAt = now;
        folder.ShareReviewSource = "AI_ASSIST";
        folder.AiReviewReason = decision.Reason;
        folder.AiReviewConfidence = decision.Confidence;
        folder.HumanReviewReason = null;
        folder.RequiresHumanReview = true;
        folder.AppealRequestedAt = null;
        folder.AppealMessage = null;
        folder.StudentFeedbackReason = null;

        if (decision.Outcome == FolderShareModerationOutcome.AutoRejected)
        {
            folder.AiReviewFailureCount += 1;
        }
    }

    private async Task EnsureFolderDocumentsApprovedAsync(Guid folderId, CancellationToken cancellationToken)
    {
        var documentCount = await _db.Documents
            .CountAsync(document => document.FolderId == folderId, cancellationToken);
        if (documentCount == 0)
        {
            throw new DocumentException(409, "folder_has_no_documents",
                "A folder must contain approved documents before it can be approved for sharing.");
        }

        var hasUnapprovedDocument = await _db.Documents
            .AnyAsync(document => document.FolderId == folderId
                && document.ReviewStatus != DocumentReviewStatus.Approved, cancellationToken);
        if (hasUnapprovedDocument)
        {
            throw new DocumentException(409, "folder_documents_not_fully_approved",
                "Every document in the folder must be approved before the folder can be approved for sharing.");
        }
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

    private static string NormalizeRequiredLongText(string? value, string code, string message)
    {
        var normalized = NormalizeModerationNote(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DocumentException(400, code, message);
        }

        return normalized;
    }

    private static string NormalizeRequiredShortText(string? value, string code, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DocumentException(400, code, message);
        }

        return normalized.Length <= 200
            ? normalized
            : normalized[..200];
    }

    private async Task<FolderSchemaCapabilities> GetFolderSchemaCapabilitiesAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
        {
            return new FolderSchemaCapabilities(true, true, true, true, true, true, true, true, true, true);
        }

        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'folders';
                """;

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    columns.Add(reader.GetString(0));
                }
            }

            var hasExtendedShareReviewColumns =
                columns.Contains("share_review_source") &&
                columns.Contains("ai_review_reason") &&
                columns.Contains("ai_review_confidence") &&
                columns.Contains("ai_review_failure_count") &&
                columns.Contains("human_review_reason") &&
                columns.Contains("requires_human_review") &&
                columns.Contains("appeal_requested_at") &&
                columns.Contains("appeal_message");

            var hasShareReviewSourceColumn = columns.Contains("share_review_source");
            var hasShareSubmissionCountColumn = columns.Contains("share_submission_count");
            var hasShareFailureCountColumn = columns.Contains("share_failure_count");
            var hasStudentFeedbackReasonColumn = columns.Contains("student_feedback_reason");
            var hasAppealRequestedAtColumn = columns.Contains("appeal_requested_at");
            var hasAppealMessageColumn = columns.Contains("appeal_message");
            var hasRequiresHumanReviewColumn = columns.Contains("requires_human_review");

            var hasStudentFeedbackWorkflowColumns =
                hasStudentFeedbackReasonColumn &&
                hasAppealMessageColumn;

            return new FolderSchemaCapabilities(
                hasExtendedShareReviewColumns,
                hasShareSubmissionCountColumn,
                hasShareFailureCountColumn,
                hasStudentFeedbackReasonColumn,
                hasAppealRequestedAtColumn,
                hasAppealMessageColumn,
                hasRequiresHumanReviewColumn,
                hasShareReviewSourceColumn,
                hasStudentFeedbackWorkflowColumns,
                hasExtendedShareReviewColumns
                && hasShareSubmissionCountColumn
                && hasShareFailureCountColumn
                && hasStudentFeedbackReasonColumn
                && hasAppealRequestedAtColumn
                && hasAppealMessageColumn
                && hasRequiresHumanReviewColumn);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
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
        ShareSubmissionCount = folder.ShareSubmissionCount,
        ShareFailureCount = folder.ShareFailureCount,
        HumanReviewReason = folder.HumanReviewReason,
        StudentFeedbackReason = folder.StudentFeedbackReason,
        RequiresHumanReview = folder.RequiresHumanReview,
        AppealRequestedAt = folder.AppealRequestedAt,
        AppealMessage = folder.AppealMessage,
        Icon = folder.Icon,
        CreatedAt = folder.CreatedAt,
        UpdatedAt = folder.UpdatedAt,
        Status = MapFolderStatus(folder.ShareStatus, folder.Documents)
    };

    private sealed record FolderSchemaCapabilities(
        bool HasExtendedShareReviewColumns,
        bool HasShareSubmissionCountColumn,
        bool HasShareFailureCountColumn,
        bool HasStudentFeedbackReasonColumn,
        bool HasAppealRequestedAtColumn,
        bool HasAppealMessageColumn,
        bool HasRequiresHumanReviewColumn,
        bool HasShareReviewSourceColumn,
        bool HasStudentFeedbackWorkflowColumns,
        bool HasFullModernShareFlowColumns);
}

