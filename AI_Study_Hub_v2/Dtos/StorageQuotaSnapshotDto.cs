namespace AI_Study_Hub_v2.Dtos;

public sealed record StorageQuotaSnapshotDto(
    long UsedBytes,
    long? QuotaBytes,
    string PlanKey,
    string PlanDisplayName,
    DateTimeOffset? ExpiresAt = null,
    long? MaxFileSizeBytes = null,
    int? MaxDocumentCount = null,
    int? MaxFolderCount = null,
    int? MaxDocsPerFolder = null,
    bool HasExpiredPaidPlan = false,
    string? ExpiredPaidPlanDisplayName = null,
    long TokensUsedToday = 0,
    long? DailyTokenQuota = 25000,
    int TotalDocumentCount = 0);
