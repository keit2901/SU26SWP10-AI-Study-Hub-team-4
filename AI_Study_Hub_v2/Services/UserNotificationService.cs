using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class UserNotificationService(AppDbContext db) : IUserNotificationService
{
    private readonly AppDbContext _db = db;

    public void StageFolderModerationFinal(Folder folder, FolderStatus previousStatus, string? rejectionReason, DateTimeOffset occurredAt)
    {
        if (previousStatus != FolderStatus.PendingShare ||
            folder.ShareStatus is not (FolderStatus.Approved or FolderStatus.Rejected))
        {
            return;
        }

        if (_db.UserNotifications.Local.Any(notification =>
                notification.RecipientUserId == folder.UserId &&
                notification.FolderId == folder.Id &&
                notification.SubmissionNumber == folder.ShareSubmissionCount))
        {
            return;
        }

        var isApproved = folder.ShareStatus == FolderStatus.Approved;
        _db.UserNotifications.Add(new UserNotification
        {
            RecipientUserId = folder.UserId,
            FolderId = folder.Id,
            SubmissionNumber = folder.ShareSubmissionCount,
            Kind = UserNotificationKind.FolderModerationFinal,
            Outcome = isApproved ? UserNotificationOutcome.Approved : UserNotificationOutcome.Rejected,
            FolderName = folder.Name,
            Title = isApproved ? "Folder approved for sharing" : "Folder sharing was not approved",
            Message = isApproved
                ? "Your folder is now available in the community."
                : CreateRejectedMessage(rejectionReason),
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
                notification.ReadAt))
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

    private static string CreateRejectedMessage(string? rejectionReason) =>
        string.IsNullOrWhiteSpace(rejectionReason)
            ? "Your folder was not approved for community sharing. Review it and submit again when ready."
            : "Your folder was not approved for community sharing. Review the moderation feedback and submit again when ready.";
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
