using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public interface IShareReviewService
{
    Task<ShareReviewSummaryDto> GetReviewAsync(Guid folderId, Guid userId, CancellationToken ct = default);
    Task<ApplyDecisionsResponse> ApplyDecisionsAsync(Guid folderId, Guid userId, ApplyDecisionsRequest request, CancellationToken ct = default);
    Task RetryShareAfterResolveAsync(Guid folderId, Guid userId, CancellationToken ct = default);
    Task<ShareRollbackResponse> TryRollbackShareAsync(Guid folderId, Guid userId, CancellationToken ct = default);
}

public sealed class ShareReviewService : IShareReviewService
{
    private readonly AppDbContext _db;
    private readonly IFolderShareAiModerator _aiModerator;

    public ShareReviewService(AppDbContext db, IFolderShareAiModerator aiModerator)
    {
        _db = db;
        _aiModerator = aiModerator;
    }

    public async Task<ShareReviewSummaryDto> GetReviewAsync(Guid folderId, Guid userId, CancellationToken ct = default)
    {
        var folder = await _db.Folders
            .Include(f => f.Documents)
            .Include(f => f.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId, ct)
            ?? throw new AdminException(404, "folder_not_found", "Folder not found or not owned by you.");

        var files = folder.Documents.Select(d => _aiModerator.EvaluateDocument(d, folder)).ToList();

        var cleanFiles = files.Count(f => f.Severity == ShareReviewSeverity.Low && !f.IsBlocked);
        var flaggedFiles = files.Count(f => f.Severity != ShareReviewSeverity.Low && !f.IsBlocked);
        var blockedFiles = files.Count(f => f.IsBlocked);
        var totalFiles = files.Count;
        var healthScore = totalFiles > 0 ? Math.Round((double)cleanFiles / totalFiles * 100, 0) : 0;
        var estimatedMinutes = Math.Max(1, (int)Math.Ceiling(flaggedFiles * 1.0));

        return new ShareReviewSummaryDto(
            folderId,
            folder.Name,
            totalFiles,
            cleanFiles,
            flaggedFiles,
            blockedFiles,
            healthScore,
            estimatedMinutes,
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
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId, ct)
            ?? throw new AdminException(404, "folder_not_found", "Folder not found.");

        if (folder.RequiresHumanReview)
        {
            folder.ShareStatus = FolderStatus.PendingShare;
            folder.ShareReviewSource = "HUMAN_REQUEST";
        }
        else
        {
            var allClean = !folder.Documents.Any(d =>
                d.ReviewStatus == DocumentReviewStatus.Rejected);
            folder.ShareStatus = allClean ? FolderStatus.Approved : FolderStatus.PendingShare;
            folder.ShareReviewSource = allClean ? "AI" : "HUMAN_REQUEST";
        }

        folder.SharedAt = folder.ShareStatus == FolderStatus.Approved ? DateTimeOffset.UtcNow : null;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ShareRollbackResponse> TryRollbackShareAsync(Guid folderId, Guid userId, CancellationToken ct = default)
    {
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId, ct)
            ?? throw new AdminException(404, "folder_not_found", "Folder not found.");

        if (folder.ShareStatus != FolderStatus.Approved || folder.SharedAt is null)
            return new ShareRollbackResponse(false, 0, false);

        var elapsed = DateTimeOffset.UtcNow - folder.SharedAt.Value;
        const int undoWindowSeconds = 30;
        var secondsRemaining = Math.Max(0, undoWindowSeconds - (int)elapsed.TotalSeconds);

        if (secondsRemaining <= 0)
            return new ShareRollbackResponse(false, 0, true);

        folder.ShareStatus = FolderStatus.PendingShare;
        folder.SharedAt = null;
        await _db.SaveChangesAsync(ct);

        return new ShareRollbackResponse(true, secondsRemaining, false);
    }
}