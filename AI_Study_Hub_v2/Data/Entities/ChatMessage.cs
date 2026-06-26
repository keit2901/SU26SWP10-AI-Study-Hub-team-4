namespace AI_Study_Hub_v2.Data.Entities;

public sealed class ChatMessage
{
    public Guid Id { get; set; }

    public Guid ChatSessionId { get; set; }

    /// <summary>"user" or "assistant"</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The message text — the user's question or the AI's answer.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>JSON blob: scope label, sources, refusal reason, duration ms.</summary>
    public string? MetadataJson { get; set; }

    /// <summary>0-based ordering within the session.</summary>
    public int SequenceNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ChatSession ChatSession { get; set; } = null!;
}
