namespace AI_Study_Hub_v2.Data.Entities;

public sealed class ChatSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Optional folder context this session was scoped to.</summary>
    public Guid? FolderId { get; set; }

    public string? Title { get; set; }

    /// <summary>e.g. "llama-3.3-70b-versatile"</summary>
    public string? Model { get; set; }

    public int TopK { get; set; } = 5;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public Folder? Folder { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
