namespace AI_Study_Hub_v2.Dtos;

public sealed class ChatSessionDto
{
    public Guid Id { get; set; }
    public Guid? FolderId { get; set; }
    public string? Title { get; set; }
    public string? Model { get; set; }
    public int TopK { get; set; }
    public int MessageCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ChatMessageDto
{
    public Guid Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public int SequenceNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateChatSessionRequest
{
    public Guid? FolderId { get; set; }
    public string? Title { get; set; }
    public string? Model { get; set; }
    public int TopK { get; set; } = 5;
}
