using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class UserNotificationService(AppDbContext db) : IUserNotificationService
{
    private const int MaxReasonPreviewLength = 160;

    private readonly AppDbContext _db = db;

    public void StageFolderModerationFinal(Folder folder, FolderStatus previousStatus, string? rejectionReason, DateTimeOffset occurredAt)
    {
        if (previousStatus != FolderStatus.PendingShare ||
            folder.ShareStatus is not (FolderStatus.Approved or FolderStatus.Rejected))
        {
            return;
        }

        var isApproved = folder.ShareStatus == FolderStatus.Approved;
        Stage(new UserNotification
        {
            RecipientUserId = folder.UserId,
            FolderId = folder.Id,
            SubmissionNumber = folder.ShareSubmissionCount,
            EventKey = $"folder-final:{folder.Id}:{folder.ShareSubmissionCount}",
            Kind = UserNotificationKind.FolderModerationFinal,
            Outcome = isApproved ? UserNotificationOutcome.Approved : UserNotificationOutcome.Rejected,
            FolderName = folder.Name,
            Title = isApproved ? "Folder approved for sharing" : "Folder sharing was not approved",
            Message = isApproved
                ? "Your folder is now available in the community."
                : "Your folder was not approved for community sharing. Review it and submit again when ready.",
            CreatedAt = occurredAt
        });
    }

    public void StageDocumentModerationFinal(
        Document document,
        Folder folder,
        string reviewerRoleLabel,
        string? reason,
        DateTimeOffset occurredAt)
    {
        if (document.FolderId != folder.Id || document.UserId != folder.UserId)
        {
            throw new InvalidOperationException("Document ownership does not match the authoritative folder owner.");
        }
        if (document.ReviewStatus is not (DocumentReviewStatus.Approved or DocumentReviewStatus.Rejected))
        {
            return;
        }

        var isApproved = document.ReviewStatus == DocumentReviewStatus.Approved;
        var normalizedRoleLabel = NormalizeReviewerRoleLabel(reviewerRoleLabel);
        Stage(new UserNotification
        {
            RecipientUserId = folder.UserId,
            FolderId = folder.Id,
            DocumentId = document.Id,
            SubmissionNumber = folder.ShareSubmissionCount,
            EventKey = $"document-final:{document.Id}:{document.ModerationGeneration}",
            Kind = UserNotificationKind.DocumentModerationFinal,
            Outcome = isApproved ? UserNotificationOutcome.Approved : UserNotificationOutcome.Rejected,
            FolderName = folder.Name,
            Title = $"{normalizedRoleLabel} {(isApproved ? "approved" : "rejected")} “{document.FileName}”",
            Message = isApproved
                ? $"{normalizedRoleLabel} approved “{document.FileName}” in “{folder.Name}” for community sharing."
                : CreateRejectedDocumentMessage(normalizedRoleLabel, document.FileName, folder.Name, reason),
            CreatedAt = occurredAt
        });
    }

    public async Task<UserNotificationFeedDto> GetMineAsync(Guid supabaseUserId, int limit, CancellationToken ct)
    {
        var userId = await GetActiveUserIdAsync(supabaseUserId, ct);
        var boundedLimit = Math.Clamp(limit, 1, 50);
        var notifications = await _db.UserNotifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(boundedLimit)
            .Select(notification => new UserNotificationFeedItemDto(
                notification.Id,
                notification.FolderId,
                notification.SubmissionNumber,
                notification.Kind,
                notification.Outcome,
                notification.FolderName,
                notification.Title,
                notification.Message,
                notification.CreatedAt,
                notification.ReadAt,
                notification.DocumentId))
            .ToListAsync(ct);
        var unreadCount = await _db.UserNotifications.CountAsync(
            notification => notification.RecipientUserId == userId && notification.ReadAt == null,
            ct);

        return new UserNotificationFeedDto(notifications, unreadCount);
    }

    public async Task MarkReadAsync(Guid supabaseUserId, Guid notificationId, CancellationToken ct)
    {
        var userId = await GetActiveUserIdAsync(supabaseUserId, ct);
        var notification = await _db.UserNotifications.FirstOrDefaultAsync(
            candidate => candidate.Id == notificationId && candidate.RecipientUserId == userId,
            ct) ?? throw UserNotificationException.NotFound();

        if (notification.ReadAt is null)
        {
            notification.ReadAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<Guid> GetActiveUserIdAsync(Guid supabaseUserId, CancellationToken ct) =>
        await _db.Users
            .AsNoTracking()
            .Where(user => user.SupabaseUserId == supabaseUserId && user.IsActive)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(ct)
        ?? throw UserNotificationException.NotFound();

    // Local tracking avoids duplicate additions in this unit of work. The database EventKey index
    // remains the authority for notifications staged by concurrent transactions.
    private void Stage(UserNotification notification)
    {
        if (!_db.UserNotifications.Local.Any(candidate => candidate.EventKey == notification.EventKey))
        {
            _db.UserNotifications.Add(notification);
        }
    }

    private static string CreateRejectedDocumentMessage(string reviewerRoleLabel, string fileName, string folderName, string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? $"{reviewerRoleLabel} rejected “{fileName}” in “{folderName}”. Review it and submit again when ready."
            : $"{reviewerRoleLabel} rejected “{fileName}” in “{folderName}”. Feedback preview: {CreateReasonPreview(reason)}";

    private static string NormalizeReviewerRoleLabel(string reviewerRoleLabel) =>
        string.Equals(reviewerRoleLabel, Role.AdminRoleName, StringComparison.OrdinalIgnoreCase)
            ? Role.AdminRoleName
            : Role.ModeratorRoleName;

    private static string CreateReasonPreview(string reason)
    {
        var normalized = new string(reason
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();
        if (normalized.Length == 0)
        {
            return "Review the moderation feedback and submit again when ready.";
        }

        return normalized.Length <= MaxReasonPreviewLength
            ? normalized
            : $"{normalized[..MaxReasonPreviewLength]}…";
    }

}

public sealed class UserNotificationException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    private UserNotificationException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public static UserNotificationException NotFound() =>
        new(404, "notification_not_found", "Notification not found.");
}
