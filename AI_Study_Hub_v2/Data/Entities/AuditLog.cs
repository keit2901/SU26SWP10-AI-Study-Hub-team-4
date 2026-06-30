namespace AI_Study_Hub_v2.Data.Entities;

public sealed class AuditLog
{
    public Guid Id { get; set; }

    public Guid? ActorUserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    public string Severity { get; set; } = "Low";

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public string? ContextJson { get; set; }

    public string? IpAddress { get; set; }

    public string? RequestId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User? ActorUser { get; set; }
}
