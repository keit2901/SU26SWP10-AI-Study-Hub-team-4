using System.ComponentModel.DataAnnotations;

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
    IReadOnlyList<DocumentEscalationItemDto> Items);

public sealed record DocumentEscalationItemDto(
    Guid DocumentId,
    string FileName,
    string RejectReason);

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
