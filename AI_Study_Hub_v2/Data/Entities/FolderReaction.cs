namespace AI_Study_Hub_v2.Data.Entities;

public sealed class FolderReaction
{
    public Guid FolderId { get; set; }

    public Guid UserId { get; set; }

    public bool IsLike { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Folder Folder { get; set; } = null!;

    public User User { get; set; } = null!;
}
