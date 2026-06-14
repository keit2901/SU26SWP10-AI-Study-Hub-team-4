namespace AI_Study_Hub_v2.Data.Entities;

public sealed class AiAnswerReport
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string ContextJson { get; set; } = "{}";

    public string SourcesJson { get; set; } = "[]";

    public string Status { get; set; } = "open";

    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
