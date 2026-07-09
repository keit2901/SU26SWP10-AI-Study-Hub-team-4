using System.ComponentModel.DataAnnotations;

namespace AI_Study_Hub_v2.Dtos;

public sealed record PlanDto(
    string PlanKey,
    string DisplayName,
    string? Description,
    long? StorageQuotaBytes,
    int? MaxDocumentCount,
    int? MaxFolderCount,
    long? DailyTokenQuota,
    long? MaxFileSizeBytes,
    int? MaxDocsPerFolder,
    long? MonthlyPriceVnd,
    long? YearlyPriceVnd);

public sealed class UpdatePlanRequest
{
    public long? StorageQuotaBytes { get; set; }
    public int? MaxDocumentCount { get; set; }
    public int? MaxFolderCount { get; set; }
    public long? DailyTokenQuota { get; set; }
    public long? MaxFileSizeBytes { get; set; }
    public int? MaxDocsPerFolder { get; set; }
}

public sealed record AssignPlanRequest(string PlanKey);

public sealed record PurchasePlanRequest(
    [Required] [StringLength(50)] string PlanKey,
    [Required] [RegularExpression(@"^(monthly|yearly)$")] string BillingCycle = "monthly",
    [StringLength(64)] string? IdempotencyKey = null);

public sealed record UserPlanDto(
    Guid Id,
    Guid PlanId,
    string PlanKey,
    string PlanDisplayName,
    string Status,
    DateTimeOffset AssignedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? PaidAt,
    StorageQuotaSnapshotDto QuotaSnapshot);

public sealed record PaymentTransactionDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string PlanKey,
    string BillingCycle,
    long AmountVnd,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
