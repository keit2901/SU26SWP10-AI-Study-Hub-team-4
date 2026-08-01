using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IUserNotificationService
{
    void StageFolderModerationFinal(Folder folder, FolderStatus previousStatus, string? rejectionReason, DateTimeOffset occurredAt);

    void StageDocumentModerationFinal(
        Document document,
        Folder folder,
        string reviewerRoleLabel,
        string? reason,
        DateTimeOffset occurredAt);

    void StageEscalationResolved(
        DocumentEscalation escalation,
        Folder folder,
        int approvedCount,
        int rejectedCount,
        DateTimeOffset occurredAt);

    Task<UserNotificationFeedDto> GetMineAsync(Guid supabaseUserId, int limit, CancellationToken ct);

    Task MarkReadAsync(Guid supabaseUserId, Guid notificationId, CancellationToken ct);
}
