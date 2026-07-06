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
