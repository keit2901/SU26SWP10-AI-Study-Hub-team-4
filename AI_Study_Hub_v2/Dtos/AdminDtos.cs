using System.ComponentModel.DataAnnotations;
using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Dtos;

public sealed record AdminUserDto(
    Guid Id,
    Guid SupabaseUserId,
    string Username,
    string FullName,
    string Role,
    bool IsActive,
    long DailyTokenQuota,
    long TokensUsedToday,
    DateOnly TokenUsageDate,
    long TotalTokensUsed,
    int DocumentCount,
    DateTimeOffset CreatedAt);

public sealed class UpdateUserQuotaRequest
{
    [Range(1_000, 10_000_000)]
    public long DailyTokenQuota { get; set; }
}

public sealed class UpdateUserRoleRequest
{
    [Required]
    [StringLength(50)]
    public string Role { get; set; } = string.Empty;
}

public sealed record AuditLogDto(
    Guid Id,
    Guid? ActorUserId,
    string ActorName,
    string Action,
    string EntityType,
    string? EntityId,
    string Severity,
    string? BeforeJson,
    string? AfterJson,
    string? ContextJson,
    string? IpAddress,
    string? RequestId,
    DateTimeOffset CreatedAt);

public sealed record AiQuotaSnapshotDto(
    long DailyTokenQuota,
    long TokensUsedToday,
    long RemainingTokens,
    DateOnly UsageDate);

public sealed record SystemConfigDto(
    string Key,
    string Value,
    string DefaultValue,
    string Category,
    string DisplayName,
    string? Description,
    string ConfigType,
    bool IsCritical,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    DateTimeOffset CreatedAt);

public sealed class UpdateSystemConfigRequest
{
    [Required]
    public string Value { get; set; } = string.Empty;
}

public sealed record DocumentEscalationDto(
    Guid Id,
    Guid FolderId,
    string EscalatedByName,
    string Reason,
    string EscalationStatus,
    string? AdminResponse,
    string? ResolvedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<DocumentEscalationItemDto> Items)
{
    public required string FolderName { get; init; }
    public string? ShareReviewSource { get; init; }
}

public sealed record DocumentEscalationItemDto(
    Guid Id,
    Guid? DocumentId,
    string FileNameSnapshot,
    int ModerationGeneration,
    string RejectReason,
    string ItemStatus,
    string? AdminResponse,
    string? ResolvedByName,
    DateTimeOffset? ResolvedAt)
{
    /// <summary>Compatibility alias for older clients; preserves the immutable file-name snapshot.</summary>
    public string FileName => FileNameSnapshot;
    public string? MimeType { get; init; }
    public long? FileSizeBytes { get; init; }
    public string? SubjectCode { get; init; }
    public string? Semester { get; init; }
    public DateTimeOffset? UploadedAt { get; init; }
    public DocumentStatus? ProcessingStatus { get; init; }
    public DocumentReviewStatus? CurrentReviewStatus { get; init; }
    public int? CurrentModerationGeneration { get; init; }
}

public sealed class CreateEscalationRequest
{
    [Required]
    public Guid FolderId { get; set; }
    [Required]
    [StringLength(2000)]
    public string Reason { get; set; } = string.Empty;
    [Required]
    [MinLength(1)]
    public List<EscalationItemRequest> Items { get; set; } = new();
}

public sealed class EscalationItemRequest
{
    [Required]
    public Guid DocumentId { get; set; }
    [Required]
    [StringLength(2000)]
    public string RejectReason { get; set; } = string.Empty;
}

public sealed class ResolveEscalationRequest
{
    [Required]
    [RegularExpression("^(Approved|Rejected)$", ErrorMessage = "Status must be 'Approved' or 'Rejected'.")]
    public string Status { get; set; } = string.Empty;
    [StringLength(2000)]
    public string? AdminResponse { get; set; }
}

/// <summary>Resolves every still-pending item in an escalation as one atomic batch.</summary>
public sealed class ResolveEscalationItemsRequest
{
    [Required]
    [MinLength(1)]
    public List<ResolveEscalationItemRequest> Items { get; set; } = new();
}

public sealed class ResolveEscalationItemRequest
{
    [Required]
    public Guid ItemId { get; set; }

    [Required]
    [RegularExpression("^(Approved|Rejected)$", ErrorMessage = "Status must be 'Approved' or 'Rejected'.")]
    public string Status { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? AdminResponse { get; set; }
}

public sealed record AdminDocumentDto(
    Guid Id,
    string FileName,
    string SubjectCode,
    string OwnerName,
    string OwnerEmail,
    string Status,
    string ReviewStatus,
    string MimeType,
    long FileSizeBytes,
    string StoragePath,
    int ChunkCount,
    DateTimeOffset CreatedAt);

public sealed record AdminDocumentDetailDto(
    Guid Id,
    string FileName,
    string SubjectCode,
    string OwnerName,
    string OwnerEmail,
    string Status,
    string ReviewStatus,
    string MimeType,
    long FileSizeBytes,
    string StoragePath,
    int ChunkCount,
    int? PageCount,
    string? ErrorMessage,
    string Semester,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DocumentChunkPreviewDto> Chunks);

public sealed record DocumentChunkPreviewDto(
    int ChunkIndex,
    string ContentPreview,
    int TokenCount,
    int? PageNumber);

public sealed class UpdateDocumentRequest
{
    [Required]
    [StringLength(500)]
    public string Title { get; set; } = string.Empty;
    [Required]
    [StringLength(50)]
    public string SubjectCode { get; set; } = string.Empty;
    [StringLength(1000)]
    public string? StoragePath { get; set; }
}
