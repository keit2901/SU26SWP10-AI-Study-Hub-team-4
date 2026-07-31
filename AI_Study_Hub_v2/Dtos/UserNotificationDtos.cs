using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Dtos;

public sealed record UserNotificationFeedItemDto(
    Guid Id,
    Guid FolderId,
    int SubmissionNumber,
    UserNotificationKind Kind,
    UserNotificationOutcome Outcome,
    string FolderName,
    string Title,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    Guid? DocumentId = null);

public sealed record UserNotificationFeedDto(
    IReadOnlyList<UserNotificationFeedItemDto> Items,
    int UnreadCount);
