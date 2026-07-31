namespace AI_Study_Hub_v2.Data.Entities;

public sealed class DocumentEscalation
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public Guid EscalatedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    /// <summary>Header lifecycle: <c>Pending</c> or <c>Resolved</c>.</summary>
    public string EscalationStatus { get; set; } = "Pending";
    public string? AdminResponse { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public Folder Folder { get; set; } = null!;
    public User EscalatedByUser { get; set; } = null!;
    public User? ResolvedByUser { get; set; }
    public ICollection<DocumentEscalationItem> Items { get; set; } = new List<DocumentEscalationItem>();
}

public sealed class DocumentEscalationItem
{
    public Guid Id { get; set; }
    public Guid EscalationId { get; set; }
    public Guid? DocumentId { get; set; }
    public string DocumentFileName { get; set; } = string.Empty;
    public int DocumentModerationGeneration { get; set; }
    public string RejectReason { get; set; } = string.Empty;
    public string ResolutionStatus { get; set; } = "Pending";
    public string? AdminResponse { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public DocumentEscalation Escalation { get; set; } = null!;
    public Document? Document { get; set; }
    public User? ResolvedByUser { get; set; }
}
