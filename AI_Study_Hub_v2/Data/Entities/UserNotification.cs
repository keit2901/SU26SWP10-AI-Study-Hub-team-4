namespace AI_Study_Hub_v2.Data.Entities;

public enum UserNotificationKind
{
    FolderModerationFinal = 0
}

public enum UserNotificationOutcome
{
    Approved = 0,
    Rejected = 1
}

public sealed class UserNotification
{
    public Guid Id { get; set; }

    public Guid RecipientUserId { get; set; }

    public Guid FolderId { get; set; }

    public int SubmissionNumber { get; set; }

    public UserNotificationKind Kind { get; set; }

    public UserNotificationOutcome Outcome { get; set; }

    public string FolderName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public User RecipientUser { get; set; } = null!;

    public Folder Folder { get; set; } = null!;
}
