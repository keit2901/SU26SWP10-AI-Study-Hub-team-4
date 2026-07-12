namespace AI_Study_Hub_v2.Data.Entities;

/// <summary>
/// User-owned folder grouping <see cref="Document"/>s. Each user has 0..N folders.
/// Documents may live without a folder (folder_id NULL) — a "loose" document.
/// </summary>
public sealed class Folder
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsFavorite { get; set; }

    public FolderStatus ShareStatus { get; set; }

    public DateTimeOffset? SharedAt { get; set; }

    public string? Icon { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public ICollection<Document> Documents { get; set; } = new List<Document>();

    public ICollection<FolderReaction> Reactions { get; set; } = new List<FolderReaction>();
}
