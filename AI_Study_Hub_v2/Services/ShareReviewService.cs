using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public interface IShareReviewService
{
    Task<ShareReviewSummaryDto> GetReviewAsync(Guid folderId, Guid userId, CancellationToken ct = default);
    Task<ShareReviewSummaryDto> GetReviewerReviewAsync(Guid folderId, Guid reviewerSupabaseUserId, CancellationToken ct = default);
    Task<IReadOnlyList<PendingShareFolderDto>> GetPendingReviewerQueueAsync(Guid reviewerSupabaseUserId, CancellationToken ct = default);
    Task<ApplyDecisionsResponse> ApplyDecisionsAsync(Guid folderId, Guid userId, ApplyDecisionsRequest request, CancellationToken ct = default);
    Task RetryShareAfterResolveAsync(Guid folderId, Guid userId, CancellationToken ct = default);
    Task<ShareRollbackResponse> TryRollbackShareAsync(Guid folderId, Guid userId, CancellationToken ct = default);
}

public sealed class ShareReviewService : IShareReviewService
{
    private readonly AppDbContext _db;

    public ShareReviewService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ShareReviewSummaryDto> GetReviewAsync(Guid folderId, Guid userId, CancellationToken ct = default)
    {
        var folder = await _db.Folders
            .Include(f => f.Documents)
            .Include(f => f.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId && f.User.SupabaseUserId == userId, ct)
            ?? throw new AdminException(404, "folder_not_found", "Folder not found or not owned by you.");

        return BuildSummary(folder);
    }

    public async Task<ShareReviewSummaryDto> GetReviewerReviewAsync(
        Guid folderId,
        Guid reviewerSupabaseUserId,
        CancellationToken ct = default)
    {
        await GetActiveReviewerAsync(reviewerSupabaseUserId, ct);

        var folder = await _db.Folders
            .Include(f => f.Documents)
            .Include(f => f.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId && f.ShareStatus == FolderStatus.PendingShare, ct)
            ?? throw new AdminException(404, "pending_share_not_found", "Pending folder share not found.");

        return BuildSummary(folder);
    }

    public async Task<IReadOnlyList<PendingShareFolderDto>> GetPendingReviewerQueueAsync(
        Guid reviewerSupabaseUserId,
        CancellationToken ct = default)
    {
        var reviewer = await GetActiveReviewerAsync(reviewerSupabaseUserId, ct);

        var folders = await _db.Folders
            .Include(folder => folder.User)
            .Include(folder => folder.Documents)
            .AsNoTracking()
            .Where(folder => folder.ShareStatus == FolderStatus.PendingShare && folder.UserId != reviewer.Id)
            .OrderByDescending(folder => folder.UpdatedAt)
            .ToListAsync(ct);

        return folders.Select(folder =>
        {
            var documents = folder.Documents
                .OrderBy(document => document.CreatedAt)
                .Select(document => new PendingShareDocumentDto(
                    document.Id,
                    document.FileName,
                    document.Status,
                    document.ReviewStatus))
                .ToList();
            var firstDocument = folder.Documents.FirstOrDefault();

            return new PendingShareFolderDto(
                folder.Id,
                folder.Name,
                string.IsNullOrWhiteSpace(folder.User?.FullName) ? folder.User?.Username ?? "Unknown" : folder.User.FullName,
                firstDocument?.SubjectCode ?? "N/A",
                firstDocument?.Semester ?? "N/A",
                documents.Count,
                folder.UpdatedAt,
                folder.ShareSubmissionCount,
                folder.ShareFailureCount,
                folder.AppealMessage,
                folder.StudentFeedbackReason,
                folder.HumanReviewReason,
                documents);
        }).ToList();
    }

    private async Task<User> GetActiveReviewerAsync(Guid reviewerSupabaseUserId, CancellationToken ct)
    {
        var reviewer = await _db.Users
            .Include(user => user.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.SupabaseUserId == reviewerSupabaseUserId, ct)
            ?? throw new AdminException(404, "user_not_found", "Authenticated user has no profile in public.users.");

        var roleName = reviewer.Role?.RoleName ?? string.Empty;
        if (!reviewer.IsActive
            || (!string.Equals(roleName, Role.AdminRoleName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(roleName, Role.ModeratorRoleName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AdminException(403, "share_reviewer_role_required",
                "Only active Admin or Moderator profiles can review pending folder shares.");
        }

        return reviewer;
    }

    private static ShareReviewSummaryDto BuildSummary(Folder folder)
    {
        var files = folder.Documents.Select(document => new ShareReviewFileDto(
            document.Id,
            document.FileName,
            document.SubjectCode,
            document.FileSizeBytes,
            document.PageCount ?? 0,
            folder.User?.FullName ?? "Unknown",
            ShareReviewSeverity.NoAiReview,
            null,
            null,
            0,
            false)).ToList();
        var totalFiles = files.Count;

        return new ShareReviewSummaryDto(
            folder.Id,
            folder.Name,
            totalFiles,
            0,
            0,
            0,
            0,
            0,
            files);
    }

    public async Task<ApplyDecisionsResponse> ApplyDecisionsAsync(Guid folderId, Guid userId, ApplyDecisionsRequest request, CancellationToken ct = default)
    {
        var folder = await _db.Folders
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId, ct)
            ?? throw new AdminException(404, "folder_not_found", "Folder not found.");

        var deletedCount = 0;
        var keptCount = 0;
        var humanReviewCount = 0;

        foreach (var v in request.Verdicts)
        {
            var doc = folder.Documents.FirstOrDefault(d => d.Id == v.DocumentId);
            if (doc is null) continue;

            switch (v.Decision)
            {
                case "Delete":
                    _db.Documents.Remove(doc);
                    deletedCount++;
                    break;
                case "HumanReview":
                    folder.RequiresHumanReview = true;
                    folder.HumanReviewReason = v.Note ?? "Student requested human review.";
                    folder.ShareStatus = FolderStatus.PendingShare;
                    folder.ShareReviewSource = "HUMAN_REQUEST";
                    folder.AppealRequestedAt = DateTimeOffset.UtcNow;
                    humanReviewCount++;
                    break;
                default:
                    keptCount++;
                    break;
            }
        }

        folder.AiReviewFailureCount = 0; // Reset after user review
        await _db.SaveChangesAsync(ct);

        var remaining = folder.Documents.Count;
        var allClean = remaining == keptCount && deletedCount == 0 && humanReviewCount == 0;

        return new ApplyDecisionsResponse(
            allClean,
            deletedCount,
            keptCount,
            humanReviewCount,
            allClean ? "All files are clean. Ready to share." : $"{deletedCount} deleted, {keptCount} kept, {humanReviewCount} sent for human review.");
    }

    public async Task RetryShareAfterResolveAsync(Guid folderId, Guid userId, CancellationToken ct = default)
    {
        var folder = await _db.Folders
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == folderId && f.User.SupabaseUserId == userId, ct)
            ?? throw new AdminException(404, "folder_not_found", "Folder not found.");

        folder.ShareStatus = FolderStatus.PendingShare;
        folder.SharedAt = null;
        folder.ShareReviewSource = "HUMAN_REQUEST";
        folder.RequiresHumanReview = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ShareRollbackResponse> TryRollbackShareAsync(Guid folderId, Guid userId, CancellationToken ct = default)
    {
        var folder = await _db.Folders
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.Id == folderId && item.User.SupabaseUserId == userId, ct)
            ?? throw new AdminException(404, "folder_not_found", "Folder not found or not owned by you.");

        if (!folder.User.IsActive)
        {
            throw new AdminException(403, "user_inactive", "Your profile is inactive.");
        }

        return new ShareRollbackResponse(false, 0, folder.ShareStatus == FolderStatus.Approved);
    }
}
