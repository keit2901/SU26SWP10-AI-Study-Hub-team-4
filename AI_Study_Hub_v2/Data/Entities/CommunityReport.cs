namespace AI_Study_Hub_v2.Data.Entities;

public sealed class CommunityReport
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public Guid ReportedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? Resolution { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    // Navigation
    public Folder Folder { get; set; } = null!;
    public User ReportedBy { get; set; } = null!;
    public User? ResolvedBy { get; set; }
}
