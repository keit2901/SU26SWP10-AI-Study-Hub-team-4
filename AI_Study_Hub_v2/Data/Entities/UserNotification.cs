namespace AI_Study_Hub_v2.Data.Entities;

public enum UserNotificationKind
{
    FolderModerationFinal = 0,
    DocumentModerationFinal = 1,
    EscalationResolved = 2
}

public enum UserNotificationOutcome
{
    Approved = 0,
    Rejected = 1,
    Mixed = 2
}

public sealed class UserNotification
{
    public Guid Id { get; set; }

    public Guid RecipientUserId { get; set; }

    public string EventKey { get; set; } = string.Empty;

    public Guid FolderId { get; set; }

    public Guid? DocumentId { get; set; }

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

    public Document? Document { get; set; }
}
